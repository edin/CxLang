namespace Cx.Compiler.Tests;

public sealed class SemanticInterfaceBindingTypeSystemTests
{
    [Fact]
    public void Compile_DeclaresInterfaceBeforeStructContainingInterfacePointer()
    {
        var result = CompilerTestHelpers.Compile(
            """
            interface AllocatorHandle {
                fn allocate(self: Self*, size: usize) -> void*;
            }

            struct Buffer {
                allocator: AllocatorHandle*;
                data: u8*;
            }

            fn main() -> int {
                let buffer: Buffer = Buffer {
                    allocator: null,
                    data: null
                };
                return buffer.data == null ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        var interfaceDeclaration = result.Output!.IndexOf(
            "typedef struct AllocatorHandle",
            StringComparison.Ordinal);
        var bufferDeclaration = result.Output.IndexOf(
            "} Buffer;",
            StringComparison.Ordinal);
        Assert.True(interfaceDeclaration >= 0);
        Assert.True(bufferDeclaration > interfaceDeclaration);
    }

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
