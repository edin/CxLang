using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Semantic;
using Cx.Compiler.Semantic.Analyzers;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class IndexedSemanticAnalyzerTests
{
    [Fact]
    public void AssignmentAnalyzer_UsesEnumFromResolvedTypeModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            enum TokenKind(value: int = 0) {
                First {}
            }
            """,
            """
            module lib.second;

            enum TokenKind(label: int = 0) {
                Second {}
            }
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var typeRefParser = new TypeRefParser(program);
        var analyzer = new AssignmentSemanticAnalyzer(
            diagnostics,
            declarations,
            new ExpressionTypeResolver(
                program,
                declarationIndex: declarations),
            new TypeCompatibility(typeRefParser),
            new TypeSystem(program),
            typeRefParser);
        var variables = new TypeEnvironment();
        variables.Set(
            "token",
            new TypeRef.Named("TokenKind", [], "lib.second"));
        var otherModuleField = CompilerTestHelpers.ParseTokenExpression(
            "token.value");
        var localModuleField = CompilerTestHelpers.ParseTokenExpression(
            "token.label");

        analyzer.AnalyzeMutationTarget(
            otherModuleField,
            otherModuleField.Location,
            variables,
            mutability: null);
        Assert.Empty(diagnostics.Diagnostics);

        analyzer.AnalyzeMutationTarget(
            localModuleField,
            localModuleField.Location,
            variables,
            mutability: null);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic =>
                diagnostic.Message.Contains(
                    "enum metadata is immutable",
                    StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "label",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ForeachAnalyzer_UsesDataEnumFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            enum TokenKind(value: int = 0) {
                First {}
            }
            """,
            """
            module lib.second;

            enum TokenKind(label: int = 0) {
                Second {}
            }

            fn inspect() -> void {
                foreach item in TokenKind {}
            }
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var typeRefParser = new TypeRefParser(program);
        var analyzer = new ForeachSemanticAnalyzer(
            diagnostics,
            declarations,
            "lib.second",
            new TypeSystem(program),
            new TypeCompatibility(typeRefParser),
            new ExpressionTypeResolver(
                program,
                declarationIndex: declarations),
            typeRefParser);
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");
        var foreachStatement = Assert.IsType<ForeachStatement>(
            Assert.Single(function.Body));

        var result = analyzer.AnalyzeForeach(
            foreachStatement,
            new TypeEnvironment(),
            new Dictionary<string, LocalMutability>(
                StringComparer.Ordinal));

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.True(result.TypeEnvironment.TryGet("item", out var itemType));
        var namedType = Assert.IsType<TypeRef.Named>(itemType);
        Assert.Equal("TokenKind", namedType.Name);
        Assert.Equal("lib.second", namedType.ModuleName);
    }

    [Fact]
    public void MatchAnalyzer_UsesTaggedUnionFromResolvedTypeModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            union Result {
                First: int;
            }
            """,
            """
            module lib.second;

            union Result {
                Second: bool;
            }

            fn inspect(result: Result) -> void {
                match result {
                    Second: value => {}
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var typeRefParser = new TypeRefParser(program);
        var analyzer = new MatchSemanticAnalyzer(
            diagnostics,
            declarations,
            "lib.second",
            new ExpressionTypeResolver(
                program,
                declarationIndex: declarations),
            typeRefParser,
            _ => true);
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");
        var matchStatement = Assert.IsType<MatchStatement>(
            Assert.Single(function.Body));
        var variables = new TypeEnvironment();
        variables.Set(
            "result",
            new TypeRef.Named("Result", [], "lib.second"));

        var binding = Assert.Single(
            analyzer.AnalyzeMatch(matchStatement, variables));

        CompilerTestHelpers.AssertNoErrors(diagnostics);
        Assert.Equal(
            "bool",
            TypeRefFormatter.ToCxString(binding.Type!));
    }

    [Fact]
    public void MatchAnalyzer_UsesInterfaceImplementationFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            interface Service {}

            struct Handler: Service {}
            """,
            """
            module lib.second;

            interface Service {}

            struct Handler {}

            fn inspect(service: Service) -> void {
                match service {
                    Handler: value => {}
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var typeRefParser = new TypeRefParser(program);
        var analyzer = new MatchSemanticAnalyzer(
            diagnostics,
            declarations,
            "lib.second",
            new ExpressionTypeResolver(
                program,
                declarationIndex: declarations),
            typeRefParser,
            _ => true);
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");
        var matchStatement = Assert.IsType<MatchStatement>(
            Assert.Single(function.Body));
        var variables = new TypeEnvironment();
        variables.Set(
            "service",
            new TypeRef.Named("Service", [], "lib.second"));

        analyzer.AnalyzeMatch(matchStatement, variables);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic =>
                diagnostic.Message.Contains(
                    "does not implement interface 'Service'",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RequirementAnalyzer_UsesRequirementFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            requires Marker<T> {}
            """,
            """
            module lib.second;

            requires Marker {}

            fn inspect<T>() -> void
            where T: Marker {}
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var analyzer = new RequirementDeclarationAnalyzer(
            diagnostics,
            program,
            declarations,
            new RequirementMatcher(program, declarations));
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");

        analyzer.AnalyzeGenericConstraints(
            function.TypeParameters,
            function.GenericConstraints,
            function.Location,
            "lib.second");

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void RequirementAnalyzer_UsesInterfaceFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            requires Marker<T> {}
            """,
            """
            module lib.second;

            interface Marker {}

            fn inspect<T>() -> void
            where T: Marker<int> {}
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var analyzer = new RequirementDeclarationAnalyzer(
            diagnostics,
            program,
            declarations,
            new RequirementMatcher(program, declarations));
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");

        analyzer.AnalyzeGenericConstraints(
            function.TypeParameters,
            function.GenericConstraints,
            function.Location,
            "lib.second");

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic =>
                diagnostic.Message.Contains(
                    "Interface 'Marker' does not take type arguments",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RequirementAnalyzer_MatchesStructRequirementInDeclaringModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            requires Marker {
                value: int;
            }
            """,
            """
            module lib.second;

            requires Marker {}

            struct Value: Marker {}
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var analyzer = new RequirementDeclarationAnalyzer(
            diagnostics,
            program,
            declarations,
            new RequirementMatcher(program, declarations));
        var structNode = Assert.Single(
            program.Structs,
            candidate =>
                candidate.Name == "Value"
                && candidate.Semantic.ModuleName == "lib.second");

        analyzer.AnalyzeStructRequirements(structNode);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void TypeUsageAnalyzer_UsesGenericStructFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Box<T> {
                value: T;
            }
            """,
            """
            module lib.second;

            requires Marker {
                value: int;
            }

            struct Box<T> where T: Marker {
                value: T;
            }

            fn inspect(value: Box<int>) -> void {}
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(program, modules);
        var analyzer = new TypeUsageAnalyzer(
            diagnostics,
            program,
            declarations,
            new RequirementMatcher(program, declarations),
            _ => true,
            _ => null,
            _ => null,
            _ => null);
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");
        var parameter = Assert.Single(function.Parameters);

        analyzer.Analyze(
            parameter.TypeNode,
            parameter.Location,
            inScopeTypeParameters: [],
            currentModuleName: "lib.second");

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic =>
                diagnostic.Message.Contains(
                    "Box.T",
                    StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "does not satisfy requirement 'Marker'",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RequirementMatcher_UsesGenericStructFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Box<T> {
                value: int;
            }
            """,
            """
            module lib.second;

            requires HasValue {
                value: bool;
            }

            struct Box<T> {
                value: bool;
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var matcher = new RequirementMatcher(
            program,
            declarations);

        var match = matcher.MatchTypeRefsFromModule(
            new TypeRef.Named(
                "Box",
                [TypeRef.Int],
                "lib.second"),
            "HasValue",
            "lib.second");

        Assert.True(
            match.Success,
            string.Join(Environment.NewLine, match.Failures));
    }

    [Fact]
    public void TypeResolver_UsesStructFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Model {
                first: int;
            }
            """,
            """
            module lib.second;

            struct Model {
                second: bool;
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var resolver = new TypeResolver(
            program,
            declarationIndex: declarations,
            currentModuleName: "lib.second");

        var resolved = resolver.Resolve(
            new TypeRef.Named("Model", []));

        var symbol = Assert.IsType<TypeSymbol.Struct>(
            resolved.Symbol);
        Assert.Equal(
            "lib.second",
            symbol.Declaration.Semantic.ModuleName);
        Assert.Equal(
            "second",
            Assert.Single(symbol.Declaration.Fields).Name);
    }

    [Fact]
    public void TypeResolver_PrefersLocalTypeAcrossDeclarationKinds()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Service {}
            """,
            """
            module lib.second;

            interface Service {}
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var resolver = new TypeResolver(
            program,
            declarationIndex: declarations,
            currentModuleName: "lib.second");

        var resolved = resolver.Resolve(
            new TypeRef.Named("Service", []));

        var symbol = Assert.IsType<TypeSymbol.Interface>(
            resolved.Symbol);
        Assert.Equal(
            "lib.second",
            symbol.Declaration.Semantic.ModuleName);
    }

    [Fact]
    public void TypeResolver_HonorsExplicitTypeModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            enum State {
                First
            }
            """,
            """
            module lib.second;

            enum State {
                Second
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var resolver = new TypeResolver(
            program,
            declarationIndex: declarations,
            currentModuleName: "lib.first");

        var resolved = resolver.Resolve(
            new TypeRef.Named(
                "State",
                [],
                "lib.second"));

        var symbol = Assert.IsType<TypeSymbol.Enum>(
            resolved.Symbol);
        Assert.Equal(
            "Second",
            Assert.Single(symbol.Declaration.Members).Name);
    }

    [Fact]
    public void MemberResolver_UsesAdapterBaseTypeFromDeclaringModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Storage<T> {
                first: int;

                fn add(value: T) -> int {
                    return 0;
                }
            }
            """,
            """
            module lib.second;

            struct Storage<T> {
                second: T;

                fn add(value: T) -> bool {
                    return true;
                }
            }

            type View<T> using Storage<T> {
                expose add as push;
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var typeResolver = new TypeResolver(
            program,
            declarationIndex: declarations,
            currentModuleName: "lib.second");
        var memberResolver =
            new ResolvedTypeMemberResolver(
                program,
                declarations,
                currentModuleName: "lib.first",
                functionCatalog:
                    FunctionCatalog.Build(program));
        var adapterType = typeResolver.ResolveDefinition(
            new TypeRef.Named(
                "View",
                [TypeRef.Int],
                "lib.second"));

        var field = Assert.Single(
            memberResolver.GetFields(adapterType));
        var method = Assert.Single(
            memberResolver.GetMethods(adapterType),
            candidate => candidate.Name == "push");

        Assert.Equal("second", field.Name);
        Assert.Equal(
            "int",
            TypeRefFormatter.ToCxString(field.Type));
        Assert.Equal(
            "bool",
            TypeRefFormatter.ToCxString(method.ReturnType));
        Assert.Equal(
            "lib.second",
            Assert.IsType<ResolvedMethodTarget.Exposed>(
                method.Target)
                .Adapter.Semantic.ModuleName);
    }

    [Fact]
    public void MemberResolver_UsesExtensionsAndOwnerFunctionsForResolvedModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            struct Gadget {}

            extension Gadget {
                fn extended() -> int {
                    return 1;
                }
            }

            fn Gadget.owned(self: Gadget*) -> int {
                return 1;
            }
            """,
            """
            module lib.second;

            struct Gadget {}

            extension Gadget {
                fn extended() -> bool {
                    return true;
                }
            }

            fn Gadget.owned(self: Gadget*) -> bool {
                return true;
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var catalog = FunctionCatalog.Build(program);
        var typeResolver = new TypeResolver(
            program,
            declarationIndex: declarations,
            currentModuleName: "lib.second");
        var memberResolver =
            new ResolvedTypeMemberResolver(
                program,
                declarations,
                functionCatalog: catalog);
        var gadgetType = typeResolver.ResolveDefinition(
            new TypeRef.Named(
                "Gadget",
                [],
                "lib.second"));

        var methods = memberResolver.GetMethods(gadgetType);

        var extended = Assert.Single(
            methods,
            candidate => candidate.Name == "extended");
        var owned = Assert.Single(
            methods,
            candidate => candidate.Name == "owned");
        Assert.Equal(
            "bool",
            TypeRefFormatter.ToCxString(
                extended.ReturnType));
        Assert.Equal(
            "bool",
            TypeRefFormatter.ToCxString(
                owned.ReturnType));
        Assert.All(
            [extended, owned],
            method => Assert.Equal(
                "lib.second",
                method.Declaration.Semantic.ModuleName));
    }

    [Fact]
    public void ReturnFlow_UsesEnumAndUnionFromCurrentModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            enum State {
                First,
                Other
            }

            union Result {
                First: int;
                Other: int;
            }
            """,
            """
            module lib.second;

            enum State {
                Second
            }

            union Result {
                Second: int;
            }

            fn choose(state: State) -> int {
                switch (state) {
                    case State.Second:
                        return 1;
                }
            }

            fn unwrap(result: Result) -> int {
                match result {
                    Second: value => {
                        return value;
                    }
                }
            }
            """);
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var resolver = new ExpressionTypeResolver(
            program,
            declarationIndex: declarations);
        var returnFlow = new ReturnFlowAnalyzer(
            declarations,
            "lib.second",
            resolver);
        var choose = Assert.Single(
            program.Functions,
            function => function.Name == "choose");
        var unwrap = Assert.Single(
            program.Functions,
            function => function.Name == "unwrap");
        var chooseEnvironment = new TypeEnvironment();
        chooseEnvironment.Set(
            "state",
            new TypeRef.Named(
                "State",
                [],
                "lib.second"));
        var unwrapEnvironment = new TypeEnvironment();
        unwrapEnvironment.Set(
            "result",
            new TypeRef.Named(
                "Result",
                [],
                "lib.second"));

        Assert.True(
            returnFlow.StatementsAlwaysReturn(
                choose.Body,
                chooseEnvironment));
        Assert.True(
            returnFlow.StatementsAlwaysReturn(
                unwrap.Body,
                unwrapEnvironment));
    }

    [Fact]
    public void DefiniteAssignment_UsesExhaustiveEnumFromFunctionModule()
    {
        var (program, modules) = CreateProgram(
            """
            module lib.first;

            enum State {
                First,
                Other
            }
            """,
            """
            module lib.second;

            enum State {
                Second
            }

            fn inspect(state: State) -> int {
                let value: int;
                switch (state) {
                    case State.Second:
                        value = 1;
                }
                return value;
            }
            """);
        var diagnostics = new DiagnosticBag();
        var declarations = ProgramDeclarationIndex.Create(
            program,
            modules);
        var resolver = new ExpressionTypeResolver(
            program,
            declarationIndex: declarations);
        var returnFlow = new ReturnFlowAnalyzer(
            declarations,
            "lib.second",
            resolver);
        var analyzer = new DefiniteAssignmentAnalyzer(
            diagnostics,
            program,
            returnFlow);
        var function = Assert.Single(
            program.Functions,
            candidate => candidate.Name == "inspect");

        analyzer.AnalyzeFunction(
            function,
            new TypeEnvironment());

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    private static (
        ProgramNode Program,
        IReadOnlyDictionary<string, string> Modules) CreateProgram(
        string firstSource,
        string secondSource)
    {
        var first = CompilerTestHelpers.Parse(firstSource, "first.cx");
        var second = CompilerTestHelpers.Parse(secondSource, "second.cx");
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
        ModuleProgramFacts.AnnotateModuleNames(program, modules);
        return (program, modules);
    }
}
