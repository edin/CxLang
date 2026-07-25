using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class FunctionCatalogTests
{
    [Fact]
    public void Build_CollectsFreeFunctionsOwnedMethodsAndExtensionMethods()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app;

            struct User {
                public fn write(value: int) -> void {}
            }

            extension User {
                fn debug() -> void {}
            }

            fn run() -> void {}
            """);

        var catalog = FunctionCatalog.Build(program);
        var userType = new TypeRef.Named("User", [], "app");

        Assert.Single(catalog.GetFunctions("run"));
        Assert.Equal(2, catalog.GetMethods(userType).Count);
        Assert.Single(catalog.GetMethods(userType, "write"));
        Assert.Single(catalog.GetMethods(userType, "debug"));
        Assert.Equal(3, catalog.DeclaredInModule("app").Count);
    }

    [Fact]
    public void GetMethods_PreservesEveryOverloadWithTheSameName()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app;

            struct Writer {
                fn write(value: int) -> void {}
                fn write(value: int, count: usize) -> void {}
                fn write(value: char*) -> void {}
            }
            """);

        var catalog = FunctionCatalog.Build(program);
        var overloads = catalog.GetMethods(
            new TypeRef.Named("Writer", [], "app"),
            "write");

        Assert.Equal(3, overloads.Count);
        Assert.Equal([1, 2, 1], overloads.Select(overload => overload.Signature.ParameterTypes.Count));
        Assert.Equal(3, overloads.Select(overload => overload.Id).Distinct().Count());
    }

    [Fact]
    public void GenericQueries_FilterKindNameAndArityWithoutLosingOverloads()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                static fn make(value: T) -> Box<T> {
                    return Box<T> { value: value };
                }

                fn map<U>(value: U) -> U {
                    return value;
                }

                fn map<U, V>(first: U, second: V) -> V {
                    return second;
                }

                fn map(value: int) -> int {
                    return value;
                }
            }

            fn convert<T>(value: T) -> T {
                return value;
            }

            fn convert(value: int) -> int {
                return value;
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var boxOfInt = new TypeRef.Named("Box", [TypeRef.Int]);

        var freeFunctions = catalog.GetGenericFunctions("convert");
        var oneParameterMethods = catalog.GetGenericMethods(
            boxOfInt,
            "map",
            genericArity: 1,
            kind: FunctionKind.Instance);
        var twoParameterMethods = catalog.GetGenericMethods(
            boxOfInt,
            "map",
            genericArity: 2,
            kind: FunctionKind.Instance);
        var staticMethods = catalog.GetGenericMethods(
            boxOfInt,
            kind: FunctionKind.Static);

        Assert.Single(freeFunctions);
        Assert.Single(oneParameterMethods);
        Assert.Single(twoParameterMethods);
        Assert.Single(staticMethods);
        Assert.Equal("make", Assert.Single(staticMethods).Name);
    }

    [Fact]
    public void Build_SeparatesReceiverAndMethodGenericArity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                fn map<U>(value: U) -> U {
                    return value;
                }
            }
            """);
        var function = Assert.Single(Assert.Single(program.Structs).Methods);
        var symbol = Assert.Single(FunctionCatalog.Build(program).Functions);

        Assert.Equal(["T"], function.ReceiverTypeParameters);
        Assert.Equal(["U"], function.MethodTypeParameters);
        Assert.Equal(["T", "U"], function.TypeParameters);
        Assert.Equal(1, symbol.Signature.ReceiverGenericArity);
        Assert.Equal(1, symbol.Signature.MethodGenericArity);
        Assert.Equal(2, symbol.Signature.TotalGenericArity);
    }

    [Fact]
    public void Build_AssignsOneIdentityWhenTheSameMethodIsAlsoHoisted()
    {
        var parsed = CompilerTestHelpers.Parse(
            """
            module app;

            struct User {
                fn debug() -> void {}
            }
            """);
        var method = Assert.Single(Assert.Single(parsed.Structs).Methods);
        var program = parsed with
        {
            Functions = parsed.Functions.Append(method).ToList(),
        };

        var catalog = FunctionCatalog.Build(program);

        var symbol = Assert.Single(catalog.Functions);
        Assert.Same(method, symbol.Declaration);
        Assert.Same(symbol, method.FunctionSymbol);
    }

    [Fact]
    public void TypeInference_RebindsNestedAndHoistedCopiesToOneCanonicalDeclaration()
    {
        var parsed = CompilerTestHelpers.Parse(
            """
            module app;

            struct User {
                fn value() -> int {
                    let result = 10;
                    return result;
                }
            }
            """);
        var method = Assert.Single(Assert.Single(parsed.Structs).Methods);
        var hoistedCopy = method with
        {
            Body = method.Body.Select(statement =>
                SyntaxNode.CloneMetadata(statement, statement with { })).ToList(),
        };
        var program = parsed with
        {
            Functions = parsed.Functions.Append(hoistedCopy).ToList(),
        };
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<FunctionCatalog>(model.FunctionCatalog);
        var symbol = Assert.Single(catalog.Functions);
        var originalId = symbol.Id;
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics, model).Resolve(program);

        var inferredProgram = new TypeInferencePass(diagnostics, model).Apply(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var inferredNestedMethod = Assert.Single(Assert.Single(inferredProgram.Structs).Methods);
        var inferredHoistedMethod = Assert.Single(inferredProgram.Functions);
        var inferredLocal = Assert.IsType<LetStatement>(inferredNestedMethod.Body[0]);
        Assert.Same(inferredNestedMethod, inferredHoistedMethod);
        Assert.Same(inferredNestedMethod, symbol.Declaration);
        Assert.Same(symbol, inferredNestedMethod.FunctionSymbol);
        Assert.Equal(originalId, symbol.Id);
        Assert.Equal("int", inferredLocal.TypeNode?.ToSourceText());
    }

    [Fact]
    public void SymbolsRetainModuleVisibility()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app;

            struct User {
                public fn debug() -> void {}
                fn reset() -> void {}
            }
            """);

        var catalog = FunctionCatalog.Build(program);
        var methods = catalog.GetMethods(new TypeRef.Named("User", [], "app"));
        var debug = Assert.Single(methods, method => method.Name == "debug");
        var reset = Assert.Single(methods, method => method.Name == "reset");

        Assert.True(debug.IsVisibleFrom("consumer"));
        Assert.True(reset.IsVisibleFrom("app"));
        Assert.False(reset.IsVisibleFrom("consumer"));
    }

    [Fact]
    public void Query_CombinesReceiverKindVisibilityAndGenericArity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app;

            struct Writer {
                public fn write(value: int) -> void {}
                fn write(value: char*) -> void {}

                public static fn create<T>(value: T) -> Writer {
                    return Writer {};
                }
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var writerType = new TypeRef.Named("Writer", [], "app");

        var consumerMethods = catalog.Query(new FunctionQuery
        {
            Name = "write",
            Kind = FunctionKind.Instance,
            ReceiverType = writerType,
            VisibleFromModule = "consumer",
        });
        var moduleMethods = catalog.Query(new FunctionQuery
        {
            Name = "write",
            Kind = FunctionKind.Instance,
            ReceiverType = writerType,
            VisibleFromModule = "app",
        });
        var genericFactories = catalog.Query(new FunctionQuery
        {
            Kind = FunctionKind.Static,
            ReceiverType = writerType,
            VisibleFromModule = "consumer",
            GenericOnly = true,
            GenericArity = 1,
        });

        Assert.Single(consumerMethods);
        Assert.Equal(2, moduleMethods.Count);
        Assert.Equal("create", Assert.Single(genericFactories).Name);
    }

    [Fact]
    public void TypeResolution_RefreshesTheExistingCanonicalSymbolInPlace()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module app;

            struct Box {
                fn echo(value: int) -> int {
                    return value;
                }
            }
            """);
        var model = CompilerTestHelpers.Resolve(program);
        var catalog = Assert.IsType<FunctionCatalog>(model.FunctionCatalog);
        var symbol = Assert.Single(catalog.Functions);
        var originalId = symbol.Id;
        var method = symbol.Declaration;
        var valueParameter = Assert.Single(
            method.Parameters,
            parameter => parameter.Name == "value");
        var diagnostics = new DiagnosticBag();

        new TypeResolutionPass(diagnostics, model).Resolve(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.True(catalog.TypesAreResolved);
        Assert.Same(symbol, method.FunctionSymbol);
        Assert.Equal(originalId, symbol.Id);
        Assert.Same(method.OwnerTypeNode?.Semantic.Type, symbol.ReceiverType);
        Assert.Same(
            valueParameter.TypeNode?.Semantic.Type,
            Assert.Single(symbol.Signature.ParameterTypes));
        Assert.Same(method.ReturnTypeNode?.Semantic.Type, symbol.Signature.ReturnType);
    }

    [Fact]
    public void GenericInstances_AreReusedByDefinitionAndStructuralTypeArguments()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var definition = Assert.Single(program.Functions);
        var created = 0;

        var first = catalog.GetOrAddInstance(
            definition,
            [new TypeRef.Named("int", [])],
            CreateDeclaration,
            out var firstAdded);
        var reused = catalog.GetOrAddInstance(
            definition,
            [new TypeRef.Named("int", [])],
            CreateDeclaration,
            out var reusedAdded);
        var different = catalog.GetOrAddInstance(
            definition,
            [new TypeRef.Pointer(TypeRef.Char)],
            CreateDeclaration,
            out var differentAdded);

        Assert.True(firstAdded);
        Assert.False(reusedAdded);
        Assert.True(differentAdded);
        Assert.Same(first, reused);
        Assert.NotSame(first, different);
        Assert.Equal(2, created);
        Assert.Equal(2, catalog.Instances.Count);
        Assert.Single(catalog.Functions);

        FunctionNode CreateDeclaration()
        {
            created++;
            return definition with { TypeParameters = [] };
        }
    }
}
