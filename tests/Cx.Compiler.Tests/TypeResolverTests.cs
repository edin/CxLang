using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class TypeResolverTests
{
    [Fact]
    public void Resolve_NamedGenericStruct_ReturnsStructSymbolAndSubstitutions()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;
                length: usize;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var parser = new TypeRefParser(program);
        var resolver = new TypeResolver(program);

        var resolved = resolver.Resolve(parser.Parse("Vec<int>"));

        var symbol = Assert.IsType<TypeSymbol.Struct>(resolved.Symbol);
        Assert.Equal("Vec", symbol.Name);
        Assert.Same(Assert.Single(program.Structs), symbol.Declaration);
        var substitution = Assert.Single(resolved.Substitutions);
        Assert.Equal("T", substitution.Key);
        Assert.Equal("int", TypeRefFormatter.ToCxString(substitution.Value));
    }

    [Fact]
    public void Resolve_Alias_ReturnsAliasSymbolAndCanResolveDefinition()
    {
        var program = CompilerTestHelpers.Parse(
            """
            type IntVec = Vec<int>;

            struct Vec<T> {
                data: T*;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var parser = new TypeRefParser(program);
        var resolver = new TypeResolver(program);
        var aliasRef = parser.Parse("IntVec");

        var resolvedAlias = resolver.Resolve(aliasRef);
        var resolvedDefinition = resolver.ResolveDefinition(aliasRef);

        Assert.IsType<TypeSymbol.Alias>(resolvedAlias.Symbol);
        var structSymbol = Assert.IsType<TypeSymbol.Struct>(resolvedDefinition.Symbol);
        Assert.Equal("Vec", structSymbol.Name);
        Assert.Equal("int", TypeRefFormatter.ToCxString(Assert.Single(resolvedDefinition.Substitutions.Values)));
    }

    [Fact]
    public void Resolve_TypeAdapter_ReturnsAdapterSymbolAndStorageSubstitutions()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;
            }

            type Stack<T> using Vec<T> {
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var parser = new TypeRefParser(program);
        var resolver = new TypeResolver(program);

        var resolved = resolver.Resolve(parser.Parse("Stack<int>"));

        var adapter = Assert.IsType<TypeSymbol.Adapter>(resolved.Symbol);
        Assert.Equal("Stack", adapter.Name);
        Assert.Equal("T", Assert.Single(resolved.Substitutions.Keys));
        Assert.Equal("int", TypeRefFormatter.ToCxString(Assert.Single(resolved.Substitutions.Values)));
    }

    [Fact]
    public void Resolve_GenericParameter_ReturnsGenericParameterSymbol()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }
            """);
        var parser = new TypeRefParser(program);
        var resolver = new TypeResolver(program, ["T"]);

        var resolved = resolver.Resolve(parser.Parse("T"));

        Assert.IsType<TypeSymbol.GenericParameter>(resolved.Symbol);
        Assert.Empty(resolved.Substitutions);
    }

    [Fact]
    public void GetFields_SubstitutesGenericParametersForResolvedStruct()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;
                length: usize;
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var parser = new TypeRefParser(program);
        var resolved = new TypeResolver(program).Resolve(parser.Parse("Vec<int>"));

        var fields = new ResolvedTypeMemberResolver(program).GetFields(resolved);

        Assert.Equal("int*", TypeRefFormatter.ToCxString(fields.Single(field => field.Name == "data").Type));
        Assert.Equal("usize", TypeRefFormatter.ToCxString(fields.Single(field => field.Name == "length").Type));
    }

    [Fact]
    public void GetMethods_SubstitutesGenericParametersForResolvedStruct()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var parser = new TypeRefParser(program);
        var resolved = new TypeResolver(program).Resolve(parser.Parse("Vec<int>"));

        var methods = new ResolvedTypeMemberResolver(program).GetMethods(resolved);

        var add = Assert.Single(methods, method => method.Name == "add");
        Assert.Equal("bool", TypeRefFormatter.ToCxString(add.ReturnType));
        Assert.Equal("int", TypeRefFormatter.ToCxString(add.ParameterTypes.Last()));
    }
}
