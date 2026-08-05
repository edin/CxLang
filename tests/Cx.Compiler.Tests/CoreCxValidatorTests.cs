using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;
using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class CoreCxValidatorTests
{
    [Fact]
    public void CoreAnnotations_RecordReceiverAndMemberAccessFacts()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Point {
                x: int;
            }

            extension Point {
                fn read(self: Self*) -> int {
                    return self.x;
                }
            }

            fn main() -> int {
                let point: Point = Point { x: 10 };
                return point.read();
            }
            """);
        var diagnostics = new DiagnosticBag();
        program = ExtensionMergePass.Apply(program, diagnostics);
        var model = new SemanticModel();
        new ScopeResolver(diagnostics, model).Resolve(program);
        new TypeResolutionPass(diagnostics, model).Resolve(program);
        program = new TypeInferencePass(diagnostics, model).Apply(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        CoreCxFunctionAnnotationPass.Apply(program);
        program.Functions.Single(function =>
                function.Name == "read").Parameters[0]
            .TypeNode!.Semantic.Type =
            new TypeRef.Pointer(new TypeRef.Named("Point", []));
        var unresolvedMainCall = Assert.IsType<CallExpressionNode>(
            program.Functions.Single(function =>
                    function.Name == "main").Body
                .OfType<ReturnStatement>()
                .Single()
                .Expression);
        Assert.IsType<MemberExpressionNode>(
                unresolvedMainCall.Callee)
            .Target.Semantic.Type =
            new TypeRef.Named("Point", []);
        CoreCxCallNormalizationPass.Apply(
            program,
            model.GetOrCreateFunctionCatalog(program));
        var normalizedCall = Assert.IsType<ResolvedCallInfo>(
            unresolvedMainCall.Semantic.ResolvedCall);
        Assert.Null(unresolvedMainCall.Semantic.CoreDirectCall);

        CoreCxCallAnnotationPass.Apply(program);
        Assert.Same(
            normalizedCall,
            unresolvedMainCall.Semantic.ResolvedCall);
        CoreCxReferenceAnnotationPass.Apply(program);
        CoreCxMemberAccessAnnotationPass.Apply(program);

        var mainCall = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ReturnStatement>(
                Assert.Single(
                    program.Functions.Single(function =>
                            function.Name == "main").Body
                        .OfType<ReturnStatement>()))
                .Expression);
        var directCall = Assert.IsType<CoreDirectCallInfo>(
            mainCall.Semantic.CoreDirectCall);
        Assert.Equal("read", directCall.Function.Name);
        Assert.Equal(
            CoreReceiverAdaptation.AddressOf,
            directCall.ReceiverAdaptation);

        var readMember = Assert.IsType<MemberExpressionNode>(
            Assert.IsType<ReturnStatement>(
                Assert.Single(
                    program.Functions.Single(function =>
                        function.Name == "read").Body))
                .Expression);
        Assert.Equal(
            CoreMemberAccessKind.Pointer,
            readMember.Semantic.CoreMemberAccess?.Kind);
    }

    [Fact]
    public void CallAnnotation_ResolvesInterfaceSlot()
    {
        var program = CompilerTestHelpers.Parse(
            """
            interface Output {
                fn write(value: int) -> bool;
            }

            fn run(output: Output*) -> bool {
                return output.write(1);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        CoreCxCallAnnotationPass.Apply(program);

        var run = program.Functions.Single(function => function.Name == "run");
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(run.Body));
        var call = Assert.IsType<CallExpressionNode>(ret.Expression);
        var interfaceCall = call.Semantic.CoreInterfaceCall;
        Assert.NotNull(interfaceCall);

        Assert.Equal("Output", interfaceCall!.Interface.Name);
        Assert.Equal("write", interfaceCall.Method.Name);
        Assert.True(interfaceCall.ReceiverIsPointer);
    }

    [Fact]
    public void CallAnnotation_RecordsStructConstructorIdentity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Point {
                x: int;
                y: int;
            }

            fn make() -> Point {
                return Point(1, 2);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        CoreCxCallAnnotationPass.Apply(program);

        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ReturnStatement>(
                Assert.Single(
                    program.Functions.Single().Body))
                .Expression);
        var constructor = Assert.IsType<
            Cx.Compiler.Semantic.CoreConstructorCallInfo.Struct>(
            call.Semantic.ConstructorCall);

        Assert.Equal("Point", constructor.Declaration.Name);
        Assert.Equal(
            "Point",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(
                constructor.ConstructedType));
    }

    [Fact]
    public void CallAnnotation_RecordsTaggedUnionConstructorIdentity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: char*;
            }

            fn make() -> Result {
                return Result.Ok(10);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        CoreCxCallAnnotationPass.Apply(program);

        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<ReturnStatement>(
                Assert.Single(
                    program.Functions.Single().Body))
                .Expression);
        var constructor = Assert.IsType<
            Cx.Compiler.Semantic.CoreConstructorCallInfo.TaggedUnion>(
            call.Semantic.ConstructorCall);

        Assert.Equal("Result", constructor.Declaration.Name);
        Assert.Equal("Ok", constructor.Variant.Name);
        Assert.Equal(
            "int",
            Cx.Compiler.Semantic.TypeRefFormatter.ToCxString(
                constructor.PayloadType));
    }

    [Fact]
    public void Validate_ReportsGenericCallInConcreteFunction()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn main() -> int {
                return identity<int>(1);
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "generic call remains after specialization",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReportsConcreteFunctionWithoutCoreTypeFacts()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "has no concrete function type facts",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_IgnoresReusableGenericTemplateBodies()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn identity<T>(value: T) -> T {
                return value;
            }

            fn forward<T>(value: T) -> T {
                return identity<T>(value);
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Analyze_ReportsForeachThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main(values: int[4]) -> void {
                foreach value: int in values {
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("foreach statement remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Pipeline_DoesNotReportSupportedLoweredForeach()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                foreach value: int in 0..4 {
                }
            }
            """);
        var diagnostics = new DiagnosticBag();
        CompilerTestHelpers.Resolve(program);

        _ = new CxPostSemanticLoweringPipeline(diagnostics).Lower(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Analyze_ReportsMatchThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            union Result {
                Ok: int;
                Error: int;
            }

            fn main(result: Result) -> void {
                match result {
                    Ok: value => {
                    }
                    Error: value => {
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("match statement remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsFunctionExpressionThatRemainsAfterLowering()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                let callback: fn(int) -> int = fn(value: int) -> int => value;
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("function expression remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Pipeline_DoesNotReportSupportedLoweredFunctionExpression()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                let callback: fn(int) -> int = fn(value: int) -> int => value;
            }
            """);
        var diagnostics = new DiagnosticBag();
        CompilerTestHelpers.Resolve(program);

        _ = new CxPostSemanticLoweringPipeline(diagnostics).Lower(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Analyze_ReportsErrorExpressionThatRemainsAfterLowering()
    {
        var location = Location.Synthetic("<lowering-completeness-test>");
        var program = new Cx.Compiler.Syntax.Nodes.ProgramNode(
            location,
            [
                new Cx.Compiler.Syntax.Nodes.FunctionNode(
                    location,
                    "main",
                    TypeParameters: [],
                    GenericConstraints: [],
                    Parameters: [],
                    Body:
                    [
                        new Cx.Compiler.Syntax.Nodes.CStatement(
                            location,
                            new Cx.Compiler.Syntax.Nodes.ErrorExpressionNode(location))
                    ],
                    Attributes: [],
                    ReturnTypeNode: Cx.Compiler.Syntax.Nodes.TypeNode.CreateFromText(location, "void")),
            ]);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("parser error expression remains after post-semantic lowering", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsNestedCompileTimeStatementResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> void {
                @if(true) {
                    @foreach value in [1] {
                        @let copy = value;
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @if statement", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @foreach statement", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time @let binding", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("compile-time list expression", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsCDeclareCompileTimeDeclarationResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare "sample.h" {
                @if(true) {
                    link "sample";
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
                diagnostic.Message.Contains("compile-time @if declaration", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsTopLevelCompileTimeDirectiveResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @if(true) {
                fn generated() -> int {
                    return 0;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "top-level compile-time @if",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_ReportsTypeMemberCompileTimeDirectiveResidue()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Value {
                @if(true) {
                    value: int;
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "compile-time @if declaration",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DoesNotInspectReusableMacroTemplates()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro emit(value: expression) -> statements {
                @if(true) {
                    consume(@{value});
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Validate_DoesNotTreatSameNamedStructAsEnumAcrossModules()
    {
        var first = CompilerTestHelpers.Parse(
            """
            module lib.first;

            enum State {
                Ready
            }
            """,
            "first.cx");
        var second = CompilerTestHelpers.Parse(
            """
            module lib.second;

            struct State {}

            fn inspect() -> void {
                State.missing;
            }
            """,
            "second.cx");
        var program = first with
        {
            Declarations = first.Declarations
                .Concat(second.Declarations)
                .ToList(),
        };
        Cx.Compiler.Modules.ModuleProgramFacts
            .AnnotateModuleNames(
                program,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["first.cx"] = "lib.first",
                    ["second.cx"] = "lib.second",
                });
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.DoesNotContain(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "static member reference 'missing'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UsesInterfaceFromTargetModule()
    {
        var program = CreateModuleProgram(
            """
            module lib.first;

            interface Service {
                fn remote() -> void;
            }
            """,
            """
            module lib.second;

            interface Service {
                fn local() -> void;
            }

            fn inspect(value: Service*) -> void {
                value.remote();
            }
            """);
        var call = Assert.Single(
            AstTraversal
                .DescendantsAndSelf(
                    program.Functions.Single(function =>
                        function.Name == "inspect"))
                .OfType<CallExpressionNode>());
        var member = Assert.IsType<MemberExpressionNode>(
            call.Callee);
        member.Target.Semantic.Type = new TypeRef.Pointer(
            new TypeRef.Named(
                "Service",
                [],
                "lib.second"));
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.DoesNotContain(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "interface call has no resolved interface slot",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DoesNotSuppressStructMemberDiagnosticForRemoteUnion()
    {
        var program = CreateModuleProgram(
            """
            module lib.first;

            union Result {
                Ok: int;
            }
            """,
            """
            module lib.second;

            struct Result {}

            fn inspect(value: Result*) -> void {
                value.missing();
            }
            """);
        var call = Assert.Single(
            AstTraversal
                .DescendantsAndSelf(
                    program.Functions.Single(function =>
                        function.Name == "inspect"))
                .OfType<CallExpressionNode>());
        var member = Assert.IsType<MemberExpressionNode>(
            call.Callee);
        member.Target.Semantic.Type = new TypeRef.Pointer(
            new TypeRef.Named(
                "Result",
                [],
                "lib.second"));
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        Assert.Contains(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "typed member call 'missing'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_KeepsImportAliasesInTheirDeclaringModule()
    {
        var program = CreateModuleProgram(
            """
            module lib.first;

            import tools as util;

            fn inspect_first() -> void {
                util.missing;
            }
            """,
            """
            module lib.second;

            fn inspect_second() -> void {
                util.missing;
            }
            """);
        var diagnostics = new DiagnosticBag();

        new CoreCxValidator(diagnostics).Validate(program);

        var diagnostic = Assert.Single(
            diagnostics.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "static member reference 'missing'",
                StringComparison.Ordinal));
        Assert.Equal(
            "first.cx",
            diagnostic.Location.File.Path);
    }

    private static ProgramNode CreateModuleProgram(
        string firstSource,
        string secondSource)
    {
        var first = CompilerTestHelpers.Parse(
            firstSource,
            "first.cx");
        var second = CompilerTestHelpers.Parse(
            secondSource,
            "second.cx");
        var program = first with
        {
            Declarations = first.Declarations
                .Concat(second.Declarations)
                .ToList(),
        };
        Cx.Compiler.Modules.ModuleProgramFacts
            .AnnotateModuleNames(
                program,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    ["first.cx"] = "lib.first",
                    ["second.cx"] = "lib.second",
                });
        return program;
    }
}
