namespace Cx.Compiler.Tests;

public sealed class SemanticInterfaceBindingTypeSystemTests
{
    [Fact]
    public void Compile_AllowsStructToInterfaceBindingThroughTypeSystem()
    {
        var result = CompilerTestHelpers.Compile(
            """
            interface ScratchAllocator {
                fn allocate(size: usize, align: usize) -> void*;
            }

            struct Arena: ScratchAllocator {
                used: usize;
            }

            extension Arena {
                fn allocate(size: usize, align: usize) -> void* {
                    return null;
                }
            }

            fn main() -> int {
                let arena: Arena = Arena { used: 0 };
                let allocator: ScratchAllocator = arena;
                return allocator.state == null;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_AllowsAliasSourceToInterfaceBindingThroughTypeSystem()
    {
        var result = CompilerTestHelpers.Compile(
            """
            type MyArena = Arena;

            interface ScratchAllocator {
                fn allocate(size: usize, align: usize) -> void*;
            }

            struct Arena: ScratchAllocator {
                used: usize;
            }

            extension Arena {
                fn allocate(size: usize, align: usize) -> void* {
                    return null;
                }
            }

            fn main() -> int {
                let arena: MyArena = Arena { used: 0 };
                let allocator: ScratchAllocator = arena;
                return allocator.state == null;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void Compile_ReportsStructThatDoesNotImplementInterface()
    {
        var result = CompilerTestHelpers.Compile(
            """
            interface ScratchAllocator {
                fn allocate(size: usize, align: usize) -> void*;
            }

            struct Arena {
                used: usize;
            }

            fn main() -> int {
                let arena: Arena = Arena { used: 0 };
                let allocator: ScratchAllocator = arena;
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(result, "Type mismatch for local 'allocator'", "cannot assign 'Arena' to 'ScratchAllocator'");
    }
}
