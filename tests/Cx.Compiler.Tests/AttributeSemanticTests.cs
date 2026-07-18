namespace Cx.Compiler.Tests;

public sealed class AttributeSemanticTests
{
    [Fact]
    public void Compile_AcceptsEvaluatorMetadataTypesAndVariableLengthLists()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute metadata on field {
                aliases: list<string>;
                enabled: bool;
                generated_name: name;
            }

            struct Item {
                @metadata(
                    aliases: { "id", "identifier", "item_id" },
                    enabled: true,
                    generated_name: as_name(concat("get_", "value"))
                )
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_AcceptsDifferentListLengthsPerAttributeApplication()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute aliases on field {
                values: list<string>;
            }

            struct Item {
                @aliases({ "one" })
                first: int;

                @aliases({ "one", "two", "three" })
                second: int;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_RejectsAttributeListWithWrongElementType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute aliases on field {
                values: list<string>;
            }

            struct Item {
                @aliases({ "one", 2 })
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "expects metadata type 'list<string>'",
            "received list");
    }

    [Fact]
    public void Compile_RejectsRuntimeTypeInAttributeSchema()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute invalid on field {
                value: char*;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Unsupported attribute metadata type 'char'");
    }

    [Fact]
    public void Compile_DeriveAttribute_IsNotBuiltIn()
    {
        var result = CompilerTestHelpers.Compile("""
            @derive(Debug)
            struct Item {
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Unknown attribute 'derive'.");
    }
}
