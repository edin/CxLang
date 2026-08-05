using Cx.Compiler.CompileTime;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ProgramDeclarationIndexTests
{
    [Fact]
    public void Lookup_ReturnsTypedDeclarationsAndExcludesFunctions()
    {
        var program = CompilerTestHelpers.Parse(
            """
            attribute metadata on field;

            requires Disposable {
                fn dispose() -> void;
            }

            enum TokenKind {
                Identifier
            }

            struct Buffer {
            }

            fn helper() -> int {
                return 0;
            }
            """);
        var index = ProgramDeclarationIndex.Create(program);

        Assert.IsType<ProgramDeclarationLookup<AttributeDeclarationNode>.Found>(
            index.Lookup<AttributeDeclarationNode>("metadata"));
        Assert.IsType<ProgramDeclarationLookup<RequirementNode>.Found>(
            index.Lookup<RequirementNode>("Disposable"));
        Assert.IsType<ProgramDeclarationLookup<EnumNode>.Found>(
            index.Lookup<EnumNode>("TokenKind"));
        Assert.IsType<ProgramDeclarationLookup<StructNode>.Found>(
            index.Lookup<StructNode>("Buffer"));
        Assert.IsType<ProgramDeclarationLookup<FunctionNode>.Missing>(
            index.Lookup<FunctionNode>("helper"));
    }

    [Fact]
    public void Lookup_PreservesAmbiguityAndResolvesByDeclaringModule()
    {
        var first = CompilerTestHelpers.Parse(
            """
            module lib.first;

            enum TokenKind {
                First
            }
            """,
            "first.cx");
        var second = CompilerTestHelpers.Parse(
            """
            module lib.second;

            enum TokenKind {
                Second
            }
            """,
            "second.cx");
        var program = first with
        {
            Declarations = first.Declarations
                .Concat(second.Declarations)
                .ToList(),
        };
        var modules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["first.cx"] = "lib.first",
            ["second.cx"] = "lib.second",
        };
        var index = ProgramDeclarationIndex.Create(program, modules);

        var ambiguous = Assert.IsType<
            ProgramDeclarationLookup<EnumNode>.Ambiguous>(
            index.Lookup<EnumNode>("TokenKind"));
        Assert.Equal(2, ambiguous.Declarations.Count);

        var firstLookup = Assert.IsType<
            ProgramDeclarationLookup<EnumNode>.Found>(
            index.LookupInModule<EnumNode>("lib.first", "TokenKind"));
        Assert.Equal("First", Assert.Single(firstLookup.Declaration.Members).Name);
        Assert.Same(
            firstLookup.Declaration,
            Assert.IsType<ProgramDeclarationLookup<EnumNode>.Found>(
                index.LookupFromModule<EnumNode>(
                    "lib.first",
                    "TokenKind"))
                .Declaration);

        var secondLookup = Assert.IsType<
            ProgramDeclarationLookup<EnumNode>.Found>(
            index.LookupInModule<EnumNode>("lib.second", "TokenKind"));
        Assert.Equal("Second", Assert.Single(secondLookup.Declaration.Members).Name);

        var reflection = new ProgramCompileTimeReflection(program, modules);
        Assert.True(reflection.TryGetEnumType(
            "TokenKind",
            out var localEnumType));
        Assert.Equal(
            "TokenKind",
            Assert.IsType<TypeRef.Named>(localEnumType).Name);
        Assert.True(reflection.TryGetEnumMembers(
            new TypeRef.Named("TokenKind", [], "lib.second"),
            out var reflectedMembers));
        Assert.Equal(
            "Second",
            Assert.Single(reflectedMembers).Declaration.Name);
    }

    [Fact]
    public void Lookup_CollapsesRepeatedProjectionOfSameDeclaration()
    {
        var source = CompilerTestHelpers.Parse(
            """
            requires Marker {
            }
            """);
        var requirement = Assert.Single(source.Requirements);
        var projected = source with
        {
            Requirements = [requirement, requirement],
        };

        var lookup = ProgramDeclarationIndex
            .Create(projected)
            .Lookup<RequirementNode>("Marker");

        Assert.IsType<ProgramDeclarationLookup<RequirementNode>.Found>(lookup);
    }
}
