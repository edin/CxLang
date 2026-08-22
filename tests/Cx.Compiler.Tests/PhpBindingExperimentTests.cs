using Cx.Compiler.Source;

namespace Cx.Compiler.Tests;

public sealed class PhpBindingExperimentTests
{
    [Fact]
    public void ExportedRequiredParameterCannotFollowOptionalParameter()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn invalid_order(
                @optional(value: 1) optional: i64,
                required: i64
            ) -> i64 {
                return optional + required;
            }

            use PhpExport(invalid_order);
            """))
            .HasDiagnostic(
                "Required PHP parameter 'required'",
                "cannot follow an optional parameter");
    }

    [Fact]
    public void ExportedFunctionCanHaveMultipleTrailingOptionalParameters()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn valid_order(
                required: i64,
                @optional(value: 1) first: i64,
                @optional(value: 2) second: i64
            ) -> i64 {
                return required + first + second;
            }

            use PhpExport(valid_order);
            """))
            .Succeeds();
    }

    [Theory]
    [InlineData("i64", "\"wrong\"", "integer", "string")]
    [InlineData("bool", "1", "boolean", "integer")]
    [InlineData("StringView", "false", "string", "boolean")]
    public void ExportedOptionalDefaultMustMatchParameterType(
        string parameterType,
        string defaultValue,
        string expectedKind,
        string actualKind)
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            $$"""
            @export
            fn invalid_default(
                @optional(value: {{defaultValue}}) value: {{parameterType}}
            ) -> i64 {
                return 0;
            }

            use PhpExport(invalid_default);
            """))
            .HasDiagnostic(
                $"parameter 'value' of type {parameterType}",
                $"requires a {expectedKind} default",
                $"received {actualKind}");
    }

    [Fact]
    public void ExportedOptionalStringViewUsesItsStringDefault()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn string_default(
                @optional(value: "fallback") value: StringView
            ) -> i64 {
                return (i64)value.length;
            }

            use PhpExport(string_default);
            """))
            .OutputContains("StringView_from_cstr(\"fallback\")");
    }

    [Fact]
    public void PhpModuleUsesConfiguredNameAndVersion()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn answer() -> i64 {
                return 42;
            }

            use PhpModule("custom_extension", "2.3.4");
            """))
            .OutputContains(
                "\"custom_extension\"",
                "\"2.3.4\"",
                "cx_generated_functions",
                "cx_generated_module_entry")
            .OutputOmits(
                "cx_demo_functions",
                "cx_demo_module_entry");
    }

    [Fact]
    public void ExportedIntegerResultReturnsValueOrThrowsPhpError()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn checked_value(success: bool) -> Result<i64, Error> {
                if (!success) {
                    return Result.err<i64, Error>(
                        Error.create("test", 1, "failed"));
                }

                return Result.ok<i64, Error>(42);
            }

            use PhpExport(checked_value);
            """))
            .OutputContains(
                "Result_is_error_",
                "zend_throw_error",
                "result.error.message",
                "ZendZval_set_long(return_value, result.value)");
    }

    [Fact]
    public void ExportedOwnedResultCopiesValueAndDisposesIt()
    {
        CompilerTestHelpers.VerifyCompilation(Sources(
            """
            @export
            fn checked_text(success: bool) -> Result<StringBuilder, Error> {
                if (!success) {
                    return Result.err<StringBuilder, Error>(
                        Error.create("test", 1, "failed"));
                }

                let text: StringBuilder = StringBuilder.create();
                text.append_cstr("owned");
                return Result.ok<StringBuilder, Error>(text);
            }

            use PhpExport(checked_text);
            """))
            .OutputContains(
                "Result_is_error_",
                "result.value",
                "ZendZval_set_string_copy",
                "Result_dispose_");
    }

    private static IReadOnlyList<SourceFile> Sources(string testSource)
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "PhpExtension");
        return
        [
            CompilerTestHelpers.Source(
                File.ReadAllText(Path.Combine(fixtureDirectory, "php85_abi.cx")),
                "php85_abi.cx"),
            CompilerTestHelpers.Source(
                File.ReadAllText(Path.Combine(fixtureDirectory, "php_binding.cx")),
                "php_binding.cx"),
            CompilerTestHelpers.Source(testSource),
        ];
    }
}
