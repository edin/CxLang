using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeDirectiveTests
{
    [Fact]
    public void Compile_CompileTimeFunctionsSupportNullableValues()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn optional_name(enabled: bool) -> string? {
                if (!enabled) {
                    return null;
                }

                return "enabled";
            }

            compile fn display_optional(value: string?) -> string {
                if (value == null) {
                    return "missing";
                }

                return value;
            }

            compile fn display_name(enabled: bool) -> string {
                let value: string? = optional_name(enabled);
                return display_optional(value);
            }

            macro emit_name() -> statements {
                consume(@{display_name(false)});
            }

            fn main() -> int {
                use emit_name();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"missing\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_CompileTimeFunctionsSupportListsOfNullableValues()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn optional_names() -> list<string?> {
                return ["first", null, "second"];
            }

            compile fn present_names() -> list<string> {
                let result: list<string> = [];
                foreach value: string? in optional_names() {
                    if (value != null) {
                        result.add(value);
                    }
                }

                return result;
            }

            macro emit_names() -> statements {
                @foreach value in present_names() {
                    consume(@{value});
                }
            }

            fn main() -> int {
                use emit_names();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"first\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"second\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_RejectsNullForNonNullableCompileTimeReturn()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile fn invalid() -> string {
                return null;
            }

            fn main() -> int {
                @let value = invalid();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "declares return type 'string' but returned null");
    }

    [Fact]
    public void Compile_RejectsNullableRuntimeTypesForNow()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main(value: string?) -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Nullable runtime type 'string?' is not supported yet",
            "limited to compile-time functions");
    }

    [Fact]
    public void Parse_ParsesCompileTimeFunctionModifier()
    {
        var program = CompilerTestHelpers.Parse(
            """
            compile fn generated_name(prefix: string, field: Field) -> string {
                return concat(prefix, field.name);
            }
            """);

        var function = Assert.Single(program.Functions);
        Assert.True(function.IsCompileTime);
        Assert.Equal("Field", function.Parameters[1].TypeNode?.ToSourceText());
    }

    [Fact]
    public void Compile_InvokesTypedCompileTimeFunctionWithReflectedField()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn generated_name(prefix: string, field: Field) -> string {
                let result: string = field.name;
                if (result == "") {
                    return prefix;
                }

                result = concat(prefix, result);
                return result;
            }

            struct User {
                id: int;
                name: const char*;
            }

            macro inspect(target: type) -> statements {
                @foreach field in target.fields {
                    consume(@{generated_name("field_", field)});
                }
            }

            fn main() -> int {
                use inspect(User);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"field_id\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"field_name\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("generated_name", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ExecutesCompileTimeFunctionListMethodsAndEarlyReturn()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn names(include_second: bool) -> list<string> {
                let result: list<string> = [];
                result.add("first");
                if (!include_second) {
                    return result;
                }

                result.add("second");
                return result;
            }

            macro emit_names() -> statements {
                @foreach value in names(false) {
                    consume(@{value});
                }
            }

            fn main() -> int {
                use emit_names();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"first\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"second\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ExecutesCompileTimeForeachOverReflectedFields()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn field_names(target: type) -> list<string> {
                let names: list<string> = [];
                if (!target.is_struct) {
                    return names;
                }

                foreach field in target.fields {
                    names.add(field.name);
                }

                return names;
            }

            struct User {
                id: int;
                name: const char*;
            }

            macro emit_fields(target: type) -> statements {
                @foreach field_name in field_names(target) {
                    consume(@{field_name});
                }
                @foreach ignored in field_names(int) {
                    consume("primitive_field");
                }
            }

            fn main() -> int {
                use emit_fields(User);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"id\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"name\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"primitive_field\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("field_names", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_CompileTimeForeachSupportsIndexBreakAndContinue()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn consume(value: const char*) -> void;

            compile fn selected_names() -> list<string> {
                let selected: list<string> = [];
                foreach index, value in ["skip", "keep", "stop", "after"] {
                    if (index == 0) {
                        continue;
                    }
                    if (value == "stop") {
                        break;
                    }

                    selected.add(value);
                }

                return selected;
            }

            macro emit_selected() -> statements {
                @foreach value in selected_names() {
                    consume(@{value});
                }
            }

            fn main() -> int {
                use emit_selected();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"keep\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"skip\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"stop\"", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"after\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ReportsNonListCompileTimeForeachIterable()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile fn invalid() -> int {
                foreach value in 42 {
                    return value;
                }

                return 0;
            }

            fn main() -> int {
                @let value = invalid();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time foreach requires a list value, but received integer");
    }

    [Fact]
    public void Compile_RejectsRuntimeTypesInCompileTimeFunctions()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct File {
                handle: int;
            }

            compile fn invalid(file: File) -> string {
                return "invalid";
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Compile-time function parameter 'file' uses unsupported type 'File'");
    }

    [Fact]
    public void Compile_ReportsCompileTimeFunctionArgumentTypeMismatch()
    {
        var result = CompilerTestHelpers.Compile(
            """
            compile fn field_name(field: Field) -> string {
                return field.name;
            }

            fn main() -> int {
                @let invalid = field_name("not a field");
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "No compile-time function 'field_name' accepts (string)");
    }

    [Fact]
    public void Compile_RejectsListExpressionOutsideCompileTimeEvaluation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let values = [1, 2];
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "List expressions are only valid during compile-time evaluation");
    }

    [Fact]
    public void Compile_RejectsTypeLiteralOutsideCompileTimeEvaluation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                let signature = Type.from(fn(int) -> int);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Type literals are only valid during compile-time evaluation");
    }

    [Fact]
    public void Parse_ParsesDedicatedCompileTimeLetNode()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro inspect(field: expression) -> statements {
                @let field_name = field.name;
                consume(@{as_name(field_name)});
            }
            """);

        var statements = Assert.Single(program.Macros).Template.Statements;
        var compileTimeLet = Assert.IsType<CompileTimeLetStatementNode>(statements[0]);

        Assert.Equal("field_name", compileTimeLet.Name);
        Assert.Equal("field.name", compileTimeLet.Initializer.ToSourceText());
        Assert.NotNull(compileTimeLet.Span);
    }

    [Fact]
    public void Parse_ParsesIfAndForeachInsideMacroTemplate()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro inspect(items: expression) -> statements {
                @if(enabled) {
                    consume(@{items});
                } else {
                    skip();
                }

                @foreach item in items {
                    consume(@{item});
                }
            }
            """);

        var statements = Assert.Single(program.Macros).Template.Statements;
        var conditional = Assert.IsType<CompileTimeIfStatementNode>(statements[0]);
        var foreachNode = Assert.IsType<CompileTimeForeachStatementNode>(statements[1]);

        Assert.Equal("enabled", conditional.Condition.ToSourceText());
        Assert.Single(conditional.ThenBlock.Items);
        Assert.Single(conditional.ElseBlock.Items);
        Assert.NotNull(conditional.ThenBlock.Span);
        Assert.NotNull(conditional.ElseBlock.Span);
        Assert.Equal("item", foreachNode.BindingName);
        Assert.Equal("items", foreachNode.IterableExpression.ToSourceText());
        Assert.IsType<PlaceholderExpressionNode>(
            Assert.Single(Assert.IsType<CallExpressionNode>(
                Assert.IsType<CStatement>(Assert.Single(foreachNode.Body.Items)).Expression).Arguments));
        Assert.NotNull(conditional.Span);
        Assert.NotNull(foreachNode.Span);
    }

    [Fact]
    public void Parse_ParsesIfAndForeachDirectlyInsideCDeclareBlock()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare "sample.h" {
                @if(target_windows) {
                    link "windows";
                } else {
                    link "portable";
                }

                @foreach library in libraries {
                    link "generated";
                }
            }
            """);

        var members = Assert.Single(program.CDeclarations).Members;
        var conditional = Assert.IsType<CompileTimeIfDeclarationNode>(members[0]);
        var foreachNode = Assert.IsType<CompileTimeForeachDeclarationNode>(members[1]);

        Assert.Equal("target_windows", conditional.Condition.ToSourceText());
        Assert.IsType<CLinkNode>(Assert.Single(conditional.ThenBlock.Items));
        Assert.IsType<CLinkNode>(Assert.Single(conditional.ElseBlock.Items));
        Assert.Equal("library", foreachNode.BindingName);
        Assert.Equal("libraries", foreachNode.IterableExpression.ToSourceText());
        Assert.IsType<CLinkNode>(Assert.Single(foreachNode.Body.Items));
        Assert.NotNull(conditional.Span);
        Assert.NotNull(foreachNode.Span);
    }

    [Fact]
    public void AstRewriter_RewritesDirectiveExpressionsAndBodies()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro sample(items: expression) -> statements {
                @if(before) {
                    @foreach item in before {
                        consume(@{before});
                    }
                }
            }
            """);

        var rewritten = new RenameRewriter().RewriteProgram(program);
        var conditional = Assert.IsType<CompileTimeIfStatementNode>(
            Assert.Single(Assert.Single(rewritten.Macros).Template.Statements));
        var foreachNode = Assert.IsType<CompileTimeForeachStatementNode>(Assert.Single(conditional.ThenBlock.Items));

        Assert.Equal("after", conditional.Condition.ToSourceText());
        Assert.Equal("after", foreachNode.IterableExpression.ToSourceText());
        var call = Assert.IsType<CallExpressionNode>(
            Assert.IsType<CStatement>(Assert.Single(foreachNode.Body.Items)).Expression);
        Assert.Equal(
            "after",
            Assert.IsType<NameExpressionNode>(
                Assert.IsType<PlaceholderExpressionNode>(Assert.Single(call.Arguments)).Expression).Name);
    }

    [Fact]
    public void CompileToC_LowersCompileTimeStatementDirective()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                @if(true) {
                    return 0;
                } else {
                    return 1;
                }

                return 2;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_RemovesCompileTimeLetBinding()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn main() -> int {
                @let selected = true;
                @if(selected) {
                    return 0;
                }

                return 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.DoesNotContain("selected", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_LowersCompileTimeCDeclarationDirective()
    {
        var result = CompilerTestHelpers.Compile(
            """
            declare "sample.h" {
                @foreach library in ["first", "second"] {
                    @if(library == "second") {
                        link "generated";
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
