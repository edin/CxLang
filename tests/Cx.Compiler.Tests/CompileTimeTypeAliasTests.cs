namespace Cx.Compiler.Tests;

public sealed class CompileTimeTypeAliasTests
{
    [Fact]
    public void CompileToC_CompileTimeCodeCanCompareReflectedAliasTypes()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            type Wide = long long;

            compile fn is_wide(value: type) -> bool {
                return value == Wide;
            }

            fn source() -> Wide {
                return (Wide)42;
            }

            macro Wrap(function: declaration) -> declarations {
                @if(is_wide(function.return_type)) {
                    fn wrapper() -> int {
                        return (int)@{as_name(function.name)}();
                    }
                }
            }

            use Wrap(source);

            fn main() -> int {
                return wrapper();
            }
            """)
            .Succeeds()
            .OutputContains("return (int) source();");
    }

    [Fact]
    public void CompileToC_CompileTimeTypeEqualityPreservesBuiltinAliasName()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            fn negate(value: bool) -> bool {
                return !value;
            }

            macro Wrap(function: declaration) -> declarations {
                @foreach parameter in function.parameters {
                    @if(parameter.type == bool) {
                        fn wrapper(value: bool) -> bool {
                            return @{as_name(function.name)}(value);
                        }
                    }
                }
            }

            use Wrap(negate);

            fn main() -> int {
                return wrapper(false);
            }
            """)
            .Succeeds()
            .OutputContains("return negate(value);");
    }

    [Fact]
    public void CompileToC_CompileTimeCodeCanConstructArraysOfSourceTypes()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            struct Item {
                value: int;
            }

            macro DeclareItems() -> declarations {
                let items: @{Type.array(Item, 2)} = {};
            }

            use DeclareItems();

            fn main() -> int {
                return items[0].value;
            }
            """)
            .Succeeds()
            .OutputContains("Item items[2]");
    }
}
