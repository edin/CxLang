using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CallResolverTests
{
    [Fact]
    public void Resolve_ResolvesFreeFunctionSignature()
    {
        var program = ParseAndResolveTypes(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            fn main() -> int {
                return add(1, 2);
            }
            """);
        var call = GetReturnCall(program);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.Equal("add", resolved.Name);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int", "int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_ResolvesAdapterExposedMethodSignature()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }

            type Stack<T> using Vec<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: Stack<int> = Stack<int> {};
                stack.push(10);
                return 0;
            }
            """);
        var statement = Assert.IsType<CStatement>(program.Functions.Single(function => function.Name == "main").Body[1]);
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("stack", "Stack<int>"), program));

        Assert.NotNull(resolved);
        Assert.Equal("Stack<int>.push", resolved.Name);
        Assert.Equal("bool", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_AcceptsTypeEnvironmentVariables()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                data: T*;
            }

            extension Vec<T> {
                fn add(value: T) -> bool {
                    return true;
                }
            }

            type Stack<T> using Vec<T> {
                expose add as push;
            }

            fn main() -> int {
                let stack: Stack<int> = Stack<int> {};
                stack.push(10);
                return 0;
            }
            """);
        var statement = Assert.IsType<CStatement>(program.Functions.Single(function => function.Name == "main").Body[1]);
        var call = Assert.IsType<CallExpressionNode>(statement.Expression);
        var resolver = CreateResolver(program);
        var parser = new TypeRefParser(program);
        var variables = new TypeEnvironment();
        variables.Set("stack", parser.Parse("Stack<int>"));

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, variables);

        Assert.NotNull(resolved);
        Assert.Equal("Stack<int>.push", resolved.Name);
        Assert.Equal("bool", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_ResolvesStaticAdapterExposedMethodToBaseFunction()
    {
        var program = ParseAndResolveTypes(
            """
            struct Vec<T> {
                static fn create() -> Vec<T> {
                    return Vec<T> {};
                }
            }

            type IntStack using Vec<int> {
                expose static create -> Self;
            }

            fn main() -> int {
                let stack: IntStack = IntStack.create();
                return 0;
            }
            """);
        var local = Assert.IsType<LetStatement>(program.Functions.Single(function => function.Name == "main").Body[0]);
        var call = Assert.IsType<CallExpressionNode>(local.Initializer);
        var resolver = CreateResolver(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.NotNull(resolved.Function);
        Assert.Equal("create", resolved.Function.Name);
        Assert.Equal("Vec", resolved.Function.OwnerTypeNode?.ToSourceText());
        Assert.False(resolved.IsInstance);
        Assert.Equal(["int"], TypeArgumentTexts(resolved.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_UsesResolvedTypeNodeForFunctionSignature()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn id(value: int) -> int {
                return value;
            }

            fn main() -> int {
                return id(1);
            }
            """);
        var location = new Location(new SourceFile("test.cx", string.Empty), Position: 0, Line: 1, Column: 1);
        var function = program.Functions.Single(function => function.Name == "id");
        var parameter = Assert.Single(function.Parameters) with
        {
            TypeNode = TypeNode.Named(location, "int"),
        };
        var rewrittenFunction = function with
        {
            ReturnTypeNode = TypeNode.Named(location, "int"),
            Parameters = [parameter],
        };
        var rewrittenProgram = program with
        {
            Functions = program.Functions
                .Select(candidate => candidate.Name == "id" ? rewrittenFunction : candidate)
                .ToList(),
        };
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(rewrittenProgram);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        var resolvedFunction = rewrittenProgram.Functions.Single(function => function.Name == "id");
        Assert.NotNull(resolvedFunction.ReturnTypeNode);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolvedFunction.ReturnTypeNode.Semantic.Type!));
        var call = GetReturnCall(rewrittenProgram);
        var resolver = CreateResolver(rewrittenProgram);

        var resolved = resolver.ResolveTypeRefs(call.Callee, [], call.Arguments, new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.Equal("int", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(["int"], resolved.ParameterTypes.Select(TypeRefFormatter.ToCxString).ToArray());
    }

    [Fact]
    public void Resolve_UsesCanonicalCatalogDeclarationInsteadOfProgramCopy()
    {
        var program = ParseAndResolveTypes(
            """
            fn value() -> int {
                return 10;
            }

            fn main() -> int {
                return value();
            }
            """);
        var original = program.Functions.Single(function => function.Name == "value");
        var catalog = FunctionCatalog.Build(program);
        var canonical = original with { };
        catalog.RebindDeclaration(original, canonical);
        var expressionTypeResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: catalog);
        var resolver = new CallResolver(
            program,
            expressionTypeResolver.ResolveTypeRef,
            functionCatalog: catalog);
        var call = GetReturnCall(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.Same(canonical, resolved.Function);
        Assert.Equal(catalog.GetSymbol(canonical).Id, canonical.FunctionSymbol?.Id);
    }

    [Fact]
    public void Resolve_SelectsFreeFunctionOverloadByArity()
    {
        var program = ParseAndResolveTypes(
            """
            fn select<T>(value: T) -> T {
                return value;
            }

            fn select<T>(value: T, fallback: T) -> T {
                return fallback;
            }

            fn main() -> int {
                return select(10, 20);
            }
            """);
        var call = GetReturnCall(program);

        var resolved = CreateResolver(program).ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.Equal(2, resolved.Function?.Parameters.Count);
        Assert.Equal(["int"], TypeArgumentTexts(resolved.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_PrefersExactTypeMatchOverCompatibleConversion()
    {
        var program = ParseAndResolveTypes(
            """
            fn format(value: int) -> int {
                return 1;
            }

            fn format(value: char) -> int {
                return 2;
            }

            fn main() -> int {
                return format(10);
            }
            """);
        var call = GetReturnCall(program);

        var resolved = CreateResolver(program).ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.Equal(
            "int",
            Assert.Single(resolved.Function!.Parameters).TypeNode.ToSourceText());
    }

    [Fact]
    public void Resolve_ReportsEquallyRankedCandidatesAsAmbiguous()
    {
        var program = ParseAndResolveTypes(
            """
            fn convert(value: char) -> int {
                return 1;
            }

            fn convert(value: long) -> int {
                return 2;
            }

            fn main() -> int {
                return convert(10);
            }
            """);
        var call = GetReturnCall(program);

        var resolved = CreateResolver(program).ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.True(resolved.IsAmbiguous);
        Assert.Null(resolved.Function);
        Assert.Equal(2, resolved.AmbiguousFunctions.Count);
    }

    [Fact]
    public void Resolve_SelectsStaticFactoryOverloadByArgumentType()
    {
        var program = ParseAndResolveTypes(
            """
            struct Value {
                static fn create(value: int) -> int {
                    return 1;
                }

                static fn create(value: char*) -> int {
                    return 2;
                }
            }

            fn main() -> int {
                return Value.create("text");
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var expressionResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: catalog);
        var resolver = new CallResolver(
            program,
            expressionResolver.ResolveTypeRef,
            functionCatalog: catalog);
        var call = GetReturnCall(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            new TypeEnvironment());

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.True(resolved.Function?.IsStatic);
        Assert.Equal(
            "char*",
            Assert.Single(resolved.Function!.Parameters).TypeNode.ToSourceText());
    }

    [Fact]
    public void Resolve_SelectsInstanceMethodOverloadByArgumentType()
    {
        var program = ParseAndResolveTypes(
            """
            struct Writer {
                fn write(value: int) -> int {
                    return 1;
                }

                fn write(value: char*) -> int {
                    return 2;
                }
            }

            fn main(writer: Writer) -> int {
                return writer.write("text");
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var expressionResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: catalog);
        var resolver = new CallResolver(
            program,
            expressionResolver.ResolveTypeRef,
            functionCatalog: catalog);
        var call = GetReturnCall(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("writer", "Writer"), program));

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.True(resolved.IsInstance);
        Assert.Equal(
            "char*",
            resolved.Function!.Parameters
                .Single(parameter => parameter.Name == "value")
                .TypeNode.ToSourceText());
    }

    [Fact]
    public void Resolve_BindsReceiverAndMethodGenericArgumentsIndependently()
    {
        var program = ParseAndResolveTypes(
            """
            struct Box<T> {
                value: T;

                fn map<U>(value: U) -> U {
                    return value;
                }
            }

            fn main(box: Box<int>) -> char* {
                return box.map("text");
            }
            """);
        var call = GetReturnCall(program);

        var resolved = CreateResolver(program).ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("box", "Box<int>"), program));

        Assert.NotNull(resolved);
        Assert.Equal("char*", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(
            ["int", "char*"],
            TypeArgumentTexts(resolved.TypeArgumentRefs));
    }

    [Fact]
    public void Resolve_AppliesExplicitArgumentsOnlyToMethodGenericsOnInstanceCall()
    {
        var program = ParseAndResolveTypes(
            """
            struct Box<T> {
                value: T;

                fn map<U>(value: U) -> U {
                    return value;
                }
            }

            fn main(box: Box<int>) -> char* {
                return box.map<char*>("text");
            }
            """);
        var main = program.Functions.Single(function => function.Name == "main");
        var statement = Assert.IsType<ReturnStatement>(Assert.Single(main.Body));
        var call = Assert.IsType<GenericCallExpressionNode>(statement.Expression);
        var parser = new TypeRefParser(program);

        var resolved = CreateResolver(program).ResolveTypeRefs(
            call.Callee,
            call.TypeArgumentNodes.Select(type => type.ToTypeRef(parser)).ToList(),
            call.Arguments,
            TypeEnvironment(("box", "Box<int>"), program));

        Assert.NotNull(resolved);
        Assert.Equal("char*", TypeRefFormatter.ToCxString(resolved.ReturnType));
        Assert.Equal(
            ["int", "char*"],
            TypeArgumentTexts(resolved.TypeArgumentRefs));
    }

    [Fact]
    public void Compiler_SpecializesMethodWithReceiverAndInferredMethodGenericArguments()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Box<T> {
                value: T;

                fn map<U>(value: U) -> U {
                    return value;
                }
            }

            fn main() -> int {
                let box: Box<int> = Box<int> { value: 10 };
                let text: char* = box.map("text");
                return text == null ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Box_map_int_char_ptr", result.Output);
    }

    [Fact]
    public void Resolve_ExcludesConstrainedExtensionOverloadWhenReceiverDoesNotSatisfyRequirement()
    {
        var program = ParseAndResolveTypes(
            """
            requires Disposable<T> {
                fn dispose(self: Self*) -> void;
            }

            struct Plain {
                value: int;
            }

            struct Box<T> {
                value: T;
            }

            extension Box<T>
            where T: Disposable<T> {
                fn select(value: int) -> int {
                    return 1;
                }
            }

            extension Box<T> {
                fn select(value: char) -> int {
                    return 2;
                }
            }

            fn main(box: Box<Plain>) -> int {
                return box.select(10);
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var expressionResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: catalog);
        var resolver = new CallResolver(
            program,
            expressionResolver.ResolveTypeRef,
            functionCatalog: catalog);
        var call = GetReturnCall(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("box", "Box<Plain>"), program));

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.Equal(
            "char",
            resolved.Function!.Parameters
                .Single(parameter => parameter.Name == "value")
                .TypeNode.ToSourceText());
    }

    [Fact]
    public void Resolve_IncludesConstrainedExtensionOverloadWhenReceiverSatisfiesRequirement()
    {
        var program = ParseAndResolveTypes(
            """
            requires Disposable<T> {
                fn dispose(self: Self*) -> void;
            }

            struct File: Disposable<File> {
                handle: void*;
            }

            extension File {
                fn dispose() -> void {
                }
            }

            struct Box<T> {
                value: T;
            }

            extension Box<T>
            where T: Disposable<T> {
                fn select(value: int) -> int {
                    return 1;
                }
            }

            extension Box<T> {
                fn select(value: char) -> int {
                    return 2;
                }
            }

            fn main(box: Box<File>) -> int {
                return box.select(10);
            }
            """);
        var catalog = FunctionCatalog.Build(program);
        var expressionResolver = new ExpressionTypeResolver(
            program,
            functionCatalog: catalog);
        var resolver = new CallResolver(
            program,
            expressionResolver.ResolveTypeRef,
            functionCatalog: catalog);
        var call = GetReturnCall(program);

        var resolved = resolver.ResolveTypeRefs(
            call.Callee,
            [],
            call.Arguments,
            TypeEnvironment(("box", "Box<File>"), program));

        Assert.NotNull(resolved);
        Assert.False(resolved.IsAmbiguous);
        Assert.Equal(
            "int",
            resolved.Function!.Parameters
                .Single(parameter => parameter.Name == "value")
                .TypeNode.ToSourceText());
    }

    [Fact]
    public void Compiler_RejectsConstrainedExtensionCallForUnsatisfiedReceiver()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Disposable<T> {
                fn dispose(self: Self*) -> void;
            }

            struct Plain {
                value: int;
            }

            struct Box<T> {
                value: T;
            }

            extension Box<T>
            where T: Disposable<T> {
                fn dispose_all() -> void {
                }
            }

            fn main(box: Box<Plain>) -> int {
                box.dispose_all();
                return 0;
            }
            """);

        Assert.False(result.Success, result.Output);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("dispose_all", StringComparison.Ordinal));
    }

    [Fact]
    public void Compiler_ReportsAmbiguousOverloadDiagnostic()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn convert(value: char) -> int {
                return 1;
            }

            fn convert(value: long) -> int {
                return 2;
            }

            fn main() -> int {
                return convert(10);
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Ambiguous call to 'convert'",
            "convert(char)",
            "convert(long)");
    }

    [Fact]
    public void Compiler_EmitsDistinctNamesForReachableOverloads()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Value {
                static fn create(value: int) -> int {
                    return value;
                }

                static fn create(value: char*) -> int {
                    return value == null ? 0 : 1;
                }
            }

            fn main() -> int {
                let number: int = Value.create(10);
                return number + Value.create("text");
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("Value_create_int", result.Output);
        Assert.Contains("Value_create_char_ptr", result.Output);
    }

    private static ProgramNode ParseAndResolveTypes(string source)
    {
        var program = CompilerTestHelpers.Parse(source);
        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
        return program;
    }

    private static CallExpressionNode GetReturnCall(ProgramNode program)
    {
        var main = program.Functions.Single(function => function.Name == "main");
        var statement = Assert.IsType<ReturnStatement>(Assert.Single(main.Body));
        return Assert.IsType<CallExpressionNode>(statement.Expression);
    }

    private static CallResolver CreateResolver(ProgramNode program) =>
        new(program, new ExpressionTypeResolver(program).ResolveTypeRef);

    private static TypeEnvironment TypeEnvironment((string Name, string Type) variable, ProgramNode program)
    {
        var parser = new TypeRefParser(program);
        var environment = new TypeEnvironment();
        environment.Set(variable.Name, parser.Parse(variable.Type));
        return environment;
    }

    private static IReadOnlyList<string> TypeArgumentTexts(IReadOnlyList<TypeRef> typeArguments) =>
        typeArguments.Select(TypeRefFormatter.ToCxString).ToList();
}
