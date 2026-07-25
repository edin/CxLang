using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using System.Text.RegularExpressions;

namespace Cx.Compiler.Tests;

public sealed class AdapterOverloadTests
{
    [Fact]
    public void TypeSystem_PreservesEveryExposedOverloadSignature()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct AdapterOverloadStorage<T> {
                fn add(value: T) -> int {
                    return 1;
                }

                fn add(value: char*) -> int {
                    return 2;
                }
            }

            type AdapterOverloadView<T> using AdapterOverloadStorage<T> {
                expose add as push;
            }
            """);
        var diagnostics = new DiagnosticBag();
        program = new CxPreSemanticLoweringPipeline(diagnostics).Lower(program);
        var model = new SemanticModel();
        new ScopeResolver(diagnostics, model).Resolve(program);
        new TypeResolutionPass(diagnostics, model).Resolve(program);
        program = new TypeInferencePass(diagnostics, model).Apply(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var methods = new TypeSystem(program)
            .GetMethods(new TypeRef.Named(
                "AdapterOverloadView",
                [TypeRef.Int]))
            .Where(method => method.Name == "push")
            .Select(method => TypeRefFormatter.ToCxString(
                method.ParameterTypes.Last()))
            .Order()
            .ToList();

        Assert.Equal(["char*", "int"], methods);
    }

    [Fact]
    public void ScopeResolver_BindsEachAdapterExposedOverload()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct AdapterOverloadStorage<T> {
                fn add(value: T) -> int {
                    return 1;
                }

                fn add(value: char*) -> int {
                    return 2;
                }
            }

            type AdapterOverloadView<T> using AdapterOverloadStorage<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: AdapterOverloadView<int> = AdapterOverloadView<int> {};
                return stack.push(10) + stack.push("text");
            }
            """);
        var diagnostics = new DiagnosticBag();
        program = new CxPreSemanticLoweringPipeline(diagnostics).Lower(program);
        var model = new SemanticModel();

        new ScopeResolver(diagnostics, model).Resolve(program);
        new TypeResolutionPass(diagnostics, model).Resolve(program);
        program = new TypeInferencePass(diagnostics, model).Apply(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var calls = Cx.Compiler.Syntax.AstExpressionTraversal
            .Enumerate(program.Functions.Single(
                function => function.Name == "main").Body)
            .OfType<CallExpressionNode>()
            .ToList();
        Assert.Equal(2, calls.Count);
        var parameterTypes = calls
            .Select(call => call.Semantic.ResolvedCall!.Function.Parameters
                .Single(parameter => parameter.Name == "value")
                .TypeNode.ToSourceText())
            .Order()
            .ToList();
        Assert.Equal(["char*", "T"], parameterTypes);
        Assert.Equal(
            2,
            calls.Select(call =>
                    call.Semantic.ResolvedCall!.Function.FunctionSymbol?.Id)
                .Distinct()
                .Count());

        var specialized = GenericSpecializationPass.Apply(
            program,
            diagnostics,
            model.FunctionCatalog)
            .Functions
            .Where(function => function.Name == "add"
                && function.TypeArgumentNodes.Count > 0)
            .ToList();
        Assert.Equal(2, specialized.Count);
    }

    [Fact]
    public void Compile_ResolvesEveryMethodExposedFromAnOverloadSet()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct AdapterOverloadStorage<T> {
                fn add(value: T) -> int {
                    return 1;
                }

                fn add(value: char*) -> int {
                    return 2;
                }
            }

            type AdapterOverloadView<T> using AdapterOverloadStorage<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: AdapterOverloadView<int> = AdapterOverloadView<int> {};
                return stack.push(10) + stack.push("text");
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var emittedOverloadNames = Regex.Matches(
                result.Output!,
                @"AdapterOverloadStorage_add[A-Za-z0-9_]*")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(
            emittedOverloadNames.Count == 2,
            $"Expected two emitted overload names, found: {string.Join(", ", emittedOverloadNames)}");
    }

    [Fact]
    public void Compile_ReportsAmbiguousAdapterExposedOverload()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct AdapterOverloadStorage<T> {
                fn add(value: char) -> int {
                    return 1;
                }

                fn add(value: long) -> int {
                    return 2;
                }
            }

            type AdapterOverloadView<T> using AdapterOverloadStorage<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: AdapterOverloadView<int> = AdapterOverloadView<int> {};
                return stack.push(10);
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Ambiguous call",
            "push",
            "AdapterOverloadStorage.add(char)",
            "AdapterOverloadStorage.add(long)");
    }

    [Fact]
    public void Compile_ResolvesStaticAdapterExposedOverloads()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct AdapterFactoryStorage<T> {
                static fn create(value: T) -> int {
                    return 1;
                }

                static fn create(value: char*) -> int {
                    return 2;
                }
            }

            type AdapterFactoryView<T> using AdapterFactoryStorage<T> {
                expose static create as make;
            }

            fn main() -> int {
                return AdapterFactoryView<int>.make(10)
                    + AdapterFactoryView<int>.make("text");
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var emittedOverloadNames = Regex.Matches(
                result.Output!,
                @"AdapterFactoryStorage_create[A-Za-z0-9_]*")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(2, emittedOverloadNames.Count);
    }
}
