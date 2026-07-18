using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class MacroExpansionPassTests
{
    [Fact]
    public void ExpandProgram_ExpandsReflectionDrivenStatementMacro()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                id: int;
                age: int;
            }

            macro debug(target: type, value: expression) -> statements {
                @foreach(field in fields(target)) {
                    debug_field(@{name(field)}, @{value}.@{name(field)});
                }
            }

            fn sample() -> int {
                use debug(User, user);
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(3, body.Count);
        Assert.Equal("debug_field(\"id\", user.id)", Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.Equal("debug_field(\"age\", user.age)", Assert.IsType<CStatement>(body[1]).Expression.ToSourceText());
        var origin = Assert.IsType<GeneratedSyntaxOrigin>(body[0].GeneratedFrom);
        Assert.Equal("use debug(User, user);", origin.InvocationSpan.Text);
        Assert.Contains("debug_field", Assert.IsType<Cx.Compiler.Source.SourceSpan>(origin.TemplateSpan).Text, StringComparison.Ordinal);
        Assert.IsType<ReturnStatement>(body[2]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_ReportsUnknownMacroAndWrongArity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro trace(value: expression) -> statements {
                emit(@{value});
            }

            fn sample() -> int {
                use missing(1);
                use trace();
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Unknown macro 'missing'", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("expects 1 argument(s), but received 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_StopsRecursiveExpansionAtDepthLimit()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro recurse() -> statements {
                use recurse();
            }

            fn sample() -> int {
                use recurse();
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("maximum depth of 64", StringComparison.Ordinal));
    }


    [Fact]
    public void CompileToC_SupportsEndToEndUserWrittenDebugMacro()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn printf(format: const char*, ...) -> int;

            struct User {
                id: int;
                age: int;
            }

            macro debug(target: type, value: expression) -> statements {
                @foreach(field in fields(target)) {
                    printf("%s=%d\\n", @{name(field)}, @{value}.@{name(field)});
                }
            }

            fn main() -> int {
                let user: User = User { id: 7, age: 42 };
                use debug(User, user);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"id\"", result.Output!, StringComparison.Ordinal);
        Assert.Contains("user.id", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"age\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("user.age", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DeclarationMacroGeneratesTypedFunctionAst()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn printf(format: const char*, ...) -> int;

            attribute debug_skip on field;

            struct User {
                id: int;
                age: int;

                @debug_skip
                secret: int;
            }

            macro generate_debug(target: type) -> declarations {
                fn debug_generated(value: @{target}) -> int {
                    @foreach(field in fields(target)) {
                        @if(!has_attribute(field, "debug_skip")) {
                            printf("%s=%d\\n", @{name(field)}, value.@{name(field)});
                        }
                    }

                    return 0;
                }
            }

            use generate_debug(User);

            fn main() -> int {
                let user: User = User { id: 7, age: 42, secret: 99 };
                return debug_generated(user);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("debug_generated", result.Output!, StringComparison.Ordinal);
        Assert.Contains("value.id", result.Output, StringComparison.Ordinal);
        Assert.Contains("value.age", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("value.secret", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandProgram_HoistsStructLocalMacroExtensionToModuleScope()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                id: int;
                use Debug(Self);
            }

            macro Debug(target: type) -> declarations {
                extension @{target} {
                    fn debug() -> int {
                        return self.id;
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var structNode = Assert.Single(expanded.Structs);
        Assert.Empty(structNode.MacroInvocationNodes);

        var extension = Assert.Single(expanded.Extensions);
        Assert.Equal("User", extension.TargetTypeNode!.ToSourceText());
        Assert.Equal("User", Assert.Single(extension.Methods).OwnerTypeNode!.ToSourceText());
        var origin = Assert.IsType<GeneratedSyntaxOrigin>(extension.GeneratedFrom);
        Assert.Equal("use Debug(Self);", origin.InvocationSpan.Text);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompileToC_DebugExtensionMacroWorksInsideOrOutsideStruct(bool invokeInsideStruct)
    {
        var source =
            """
            extern fn printf(format: const char*, ...) -> int;

            struct User {
                id: int;
                age: int;
                $INSIDE$
            }

            macro Debug(target: type) -> declarations {
                extension @{target} {
                    fn debug() -> int {
                        @foreach(field in fields(target)) {
                            @let field_name = field.name;
                            printf("%s=%d\\n", @{field_name}, self.@{field_name});
                        }

                        return 0;
                    }
                }
            }

            $OUTSIDE$

            fn main() -> int {
                let user: User = User { id: 7, age: 42 };
                return user.debug();
            }
            """
            .Replace("$INSIDE$", invokeInsideStruct ? "use Debug(Self);" : string.Empty, StringComparison.Ordinal)
            .Replace("$OUTSIDE$", invokeInsideStruct ? string.Empty : "use Debug(User);", StringComparison.Ordinal);

        var result = CompilerTestHelpers.Compile(source);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("debug", result.Output!, StringComparison.Ordinal);
        Assert.Contains("self->id", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->age", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DebugWriterMacroSupportsPrimitiveAndNestedStructFields()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*, output: StringBuilder*) -> bool;
            }

            extension int {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_int(*self);
                }
            }

            extension bool {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_bool(*self);
                }
            }

            extension double {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_double(*self);
                }
            }

            struct Address {
                number: int;

                use Debug(Self);
            }

            struct User {
                id: int;
                active: bool;
                score: double;
                address: Address;

                use Debug(Self);
            }

            macro Debug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn write_debug(output: StringBuilder*) -> bool {
                        if (!output.append_cstr(@{concat(target.name, " { ")})) {
                            return false;
                        }

                        let first: bool = true;
                        @foreach(field in target.fields) {
                            @let field_name = field.name;
                            if (!first && !output.append_cstr(", ")) {
                                return false;
                            }
                            first = false;

                            if (!output.append_cstr(@{concat(field.name, ": ")})) {
                                return false;
                            }

                            @let debug_match = requirement_match(field.type, Debug);
                            @if(!debug_match.success) {
                                @let _ = compile_error(concat(
                                    "Debug cannot generate '",
                                    target.name,
                                    ".",
                                    field.name,
                                    "': unsupported field type '",
                                    field.type.display_name,
                                    "'."));
                            }
                            @if(debug_match.success) {
                                if (!self.@{field_name}.write_debug(output)) {
                                    return false;
                                }
                            }
                        }

                        return output.append_cstr(" }");
                    }
                }
            }

            fn main() -> int {
                let builder: StringBuilder = StringBuilder.with_capacity((usize)32);
                let user: User = User {
                    id: 7,
                    active: true,
                    score: 3.5,
                    address: Address { number: 42 },
                };
                let ok: bool = user.write_debug(&builder);
                builder.free();
                return ok ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("self->id", result.Output!, StringComparison.Ordinal);
        Assert.Contains("self->active", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->score", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->address", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->number", result.Output, StringComparison.Ordinal);
        Assert.Contains("append_int", result.Output!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DebugMacroRejectsUnsupportedFieldType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*) -> bool;
            }

            struct User {
                count: usize;
                use Debug(Self);
            }

            macro Debug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn write_debug() -> bool {
                        @foreach(field in target.fields) {
                            @let debug_match = requirement_match(field.type, Debug);
                            @if(!debug_match.success) {
                                @let _ = compile_error(concat(
                                    "Debug cannot generate '",
                                    target.name,
                                    ".",
                                    field.name,
                                    "': unsupported field type '",
                                    field.type.display_name,
                                    "'."));
                            }
                        }

                        return true;
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Debug cannot generate 'User.count': unsupported field type 'usize'.");
    }

    [Fact]
    public void CompileToC_RejectsUnfulfilledMacroProvidedRequirement()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*) -> bool;
            }

            struct User {
                use BrokenDebug(Self);
            }

            macro BrokenDebug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn unrelated() -> bool {
                        return true;
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Macro 'BrokenDebug' claims that 'User' provides 'Debug'",
            "Missing function 'write_debug'");
    }
}
