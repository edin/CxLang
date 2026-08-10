using Cx.Compiler.Lowering;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeDirectiveTests
{
    [Fact]
    public void Compile_ExpandsDirectivesAtModuleTopLevel()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile const enabled: bool = true;
            compile const names: list<string> = ["first", "second"];

            @if(enabled) {
                @foreach name in names {
                    fn @{as_name(name)}() -> int {
                        return 1;
                    }
                }
            }

            fn main() -> int {
                return first() + second();
            }
            """)
            .Succeeds()
            .OutputContains("first(", "second(")
            .OutputOmits("@if", "@foreach");
    }

    [Fact]
    public void Compile_ExpandsSelectedTopLevelMacroInvocationOnly()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Generate() -> declarations {
                fn generated() -> int {
                    return 42;
                }
            }

            @if(false) {
                use MissingMacro();
            } else {
                use Generate();
            }

            fn main() -> int {
                return generated();
            }
            """)
            .Succeeds()
            .OutputContains("generated(")
            .OutputOmits("MissingMacro");
    }

    [Fact]
    public void Compile_ExpandsGenericDependentDirectiveForEachSpecialization()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn selected<T>(value: T) -> int {
                @if(T == int) {
                    return 1;
                } else {
                    return 2;
                }
            }

            fn main() -> int {
                return selected<int>(0);
            }
            """)
            .Succeeds()
            .OutputOmits("@if");
    }

    [Fact]
    public void Compile_ExpandsGenericRequirementMatchAndResolvesGeneratedCalls()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
            """
            struct Resource: Disposable<Resource> {
                disposed: bool;
            }

            extension Resource {
                fn dispose() -> void {
                    self.disposed = true;
                }
            }

            fn dispose_value<T>(value: T*) -> void {
                @let match = T.match(Disposable);

                @if(match.success) {
                    value.dispose();
                }
            }

            fn main() -> int {
                let resource = Resource { disposed: false };
                let number = 42;
                dispose_value<Resource>(&resource);
                dispose_value<int>(&number);
                return resource.disposed ? 0 : 1;
            }
            """)
            .Succeeds()
            .OutputOmits("@if");

        Assert.Equal(
            1,
            test.Result.Output!.Split(
                "Resource_dispose(value);",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Compile_PreservesDeferredCompileTimeMutationsUntilSpecialization()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            extern fn consume(value: const char*) -> void;

            struct User {
                id: int;
            }

            fn inspect<T>() -> int {
                @let count = T.fields.count;
                count = count == 1 ? 2 : 3;

                @let members = T.fields;
                members.add(Parameter.create("extra", int));
                @foreach member in members {
                    consume(@{member.name});
                }

                @if(count == 2) {
                    return 2;
                } else {
                    return 1;
                }
            }

            fn main() -> int {
                return inspect<User>();
            }
            """)
            .Succeeds()
            .OutputContains("\"id\"", "\"extra\"", "return 2;");
    }

    [Fact]
    public void Compile_ExpandsGenericDependentForeachForEachSpecialization()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            extern fn consume(value: const char*) -> void;

            struct User {
                id: int;
                age: int;
            }

            fn inspect<T>(value: T*) -> int {
                @let fields = T.fields;
                @foreach field in fields {
                    consume(@{field.name});
                }

                return 0;
            }

            fn main() -> int {
                let user = User { id: 1, age: 2 };
                return inspect<User>(&user);
            }
            """)
            .Succeeds()
            .OutputContains("\"id\"", "\"age\"")
            .OutputOmits("@foreach");
    }

    [Fact]
    public void Compile_ExpandsCompileTimeDirectivesInsideStructBody()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Value {
                data: int;

                @if(true) {
                    extra: int;

                    fn total() -> int {
                        return self.data + self.extra;
                    }
                }
            }

            fn main() -> int {
                let value = Value { data: 10, extra: 20 };
                return value.total();
            }
            """)
            .Succeeds()
            .OutputContains("extra")
            .OutputOmits("@if");
    }

    [Fact]
    public void Compile_ExpandsCompileTimeForeachInsideExtensionBody()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Value {
                first: int;
                second: int;
            }

            extension Value {
                @foreach field in Self.fields {
                    fn @{as_name(concat("get_", field.name))}() -> int {
                        return self.@{field.name};
                    }
                }
            }

            fn main() -> int {
                let value = Value { first: 10, second: 20 };
                return value.get_first() + value.get_second();
            }
            """)
            .Succeeds()
            .OutputContains("get_first", "get_second")
            .OutputOmits("@foreach");
    }

    [Fact]
    public void Compile_ExpandsMacroInvocationGeneratedInsideStructDirective()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro AddGenerated(target: type) -> declarations {
                extension @{target} {
                    fn generated() -> int {
                        return self.data;
                    }
                }
            }

            struct Value {
                data: int;

                @if(true) {
                    use AddGenerated(Self);
                }
            }

            fn main() -> int {
                let value = Value { data: 42 };
                return value.generated();
            }
            """)
            .Succeeds()
            .OutputContains("generated")
            .OutputOmits("AddGenerated");
    }

    [Fact]
    public void Compile_ExpandsCompileTimeDirectiveInsideTypeAdapterBody()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Storage {
                data: int;

                static fn create() -> Storage {
                    return Storage { data: 42 };
                }
            }

            type View using Storage {
                @if(true) {
                    expose static create -> Self;
                }
            }

            fn main() -> int {
                let value: View = View.create();
                return value.data;
            }
            """)
            .Succeeds()
            .OutputContains("Storage_create")
            .OutputOmits("@if");
    }

    [Fact]
    public void Compile_CompileTimeFunctionsSupportNullableValues()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"missing\"");
    }

    [Fact]
    public void Compile_CompileTimeFunctionsSupportListsOfNullableValues()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"first\"", "\"second\"");
    }

    [Fact]
    public void Compile_RejectsNullForNonNullableCompileTimeReturn()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile fn invalid() -> string {
                return null;
            }

            fn main() -> int {
                @let value = invalid();
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("declares return type 'string' but returned null");
    }

    [Fact]
    public void Compile_RejectsNullableRuntimeTypesForNow()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main(value: string?) -> int {
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic(
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
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"field_id\"", "\"field_name\"")
            .OutputOmits("generated_name");
    }

    [Fact]
    public void Compile_ExecutesCompileTimeFunctionListMethodsAndEarlyReturn()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"first\"")
            .OutputOmits("\"second\"");
    }

    [Fact]
    public void Compile_ExecutesCompileTimeForeachOverReflectedFields()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"id\"", "\"name\"")
            .OutputOmits("\"primitive_field\"", "field_names");
    }

    [Fact]
    public void Compile_CompileTimeForeachSupportsIndexBreakAndContinue()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds()
            .OutputContains("\"keep\"")
            .OutputOmits("\"skip\"", "\"stop\"", "\"after\"");
    }

    [Fact]
    public void Compile_ReportsNonListCompileTimeForeachIterable()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Fails()
            .HasDiagnostic("Compile-time foreach requires a list value, but received integer");
    }

    [Fact]
    public void Compile_RejectsRuntimeTypesInCompileTimeFunctions()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Fails()
            .HasDiagnostic("Compile-time function parameter 'file' uses unsupported type 'File'");
    }

    [Fact]
    public void Compile_ReportsCompileTimeFunctionArgumentTypeMismatch()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            compile fn field_name(field: Field) -> string {
                return field.name;
            }

            fn main() -> int {
                @let invalid = field_name("not a field");
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("No compile-time function 'field_name' accepts (string)");
    }

    [Fact]
    public void Compile_RejectsListExpressionOutsideCompileTimeEvaluation()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let values = [1, 2];
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("List expressions are only valid during compile-time evaluation");
    }

    [Fact]
    public void Compile_RejectsTypeLiteralOutsideCompileTimeEvaluation()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                let signature = Type.from(fn(int) -> int);
                return 0;
            }
            """)
            .Fails()
            .HasDiagnostic("Type literals are only valid during compile-time evaluation");
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
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                @if(true) {
                    return 0;
                } else {
                    return 1;
                }

                return 2;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void CompileToC_RemovesCompileTimeLetBinding()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn main() -> int {
                @let selected = true;
                @if(selected) {
                    return 0;
                }

                return 1;
            }
            """)
            .Succeeds()
            .OutputOmits("selected");
    }

    [Fact]
    public void CompileToC_LowersCompileTimeCDeclarationDirective()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds();
    }

    private sealed class RenameRewriter : AstRewriter
    {
        protected override ExpressionNode RewriteNameExpression(NameExpressionNode name) =>
            name.Name == "before" ? name with { Name = "after" } : name;
    }
}
