namespace Cx.Compiler.Tests;

public sealed class AttributeSemanticTests
{
    [Fact]
    public void Compile_AllowsCompileTimeFunctionsInAttributeArguments()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute route on fn {
                path: string;
            }

            compile fn route_path(resource: string) -> string? {
                if (resource == "") {
                    return null;
                }

                let path: string = concat("/", resource);
                return path;
            }

            @route(path: route_path("users"))
            fn users() -> void {
            }

            fn main() -> int {
                return 0;
            }
            """)
            .Succeeds()
            .OutputOmits("route_path");
    }

    [Fact]
    public void Compile_AcceptsEvaluatorMetadataTypesAndVariableLengthLists()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute metadata on field {
                aliases: list<string>;
                groups: list<list<string>>;
                enabled: bool;
                generated_name: name;
            }

            struct Item {
                @metadata(
                    aliases: ["id", "identifier", "item_id"],
                    groups: [["core", "public"], ["generated"]],
                    enabled: true,
                    generated_name: as_name(concat("get_", "value"))
                )
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_AcceptsDifferentListLengthsPerAttributeApplication()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute aliases on field {
                values: list<string>;
            }

            struct Item {
                @aliases(["one"])
                first: int;

                @aliases(["one", "two", "three"])
                second: int;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .Succeeds();
    }

    [Fact]
    public void Compile_RejectsAttributeListWithWrongElementType()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute aliases on field {
                values: list<string>;
            }

            struct Item {
                @aliases(["one", 2])
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith(
                "expects metadata type 'list<string>'",
                "received list");
    }

    [Fact]
    public void Compile_RejectsRuntimeTypeInAttributeSchema()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            attribute invalid on field {
                value: char*;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith(
                "Unsupported attribute metadata type 'char'");
    }

    [Fact]
    public void Compile_DeriveAttribute_IsNotBuiltIn()
    {
        CompilerTestHelpers.VerifyCompilation("""
            @derive(Debug)
            struct Item {
                value: int;
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith("Unknown attribute 'derive'.");
    }

    [Fact]
    public void Compile_ValidatesAttributesOnNestedMethods()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Item {
                @unknown
                fn inspect() -> void {}
            }

            fn main() -> int {
                return 0;
            }
            """)
            .FailsWith("Unknown attribute 'unknown'.");
    }
}
