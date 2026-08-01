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
        Assert.Contains(".state = &arena", result.Output);
        Assert.Contains(
            ".vtable = &Arena_ScratchAllocator_vtable",
            result.Output);
        Assert.Contains(".allocate =", result.Output);
        Assert.Contains("Arena_allocate", result.Output);
    }

    [Fact]
    public void Compile_LowersInterfaceConversionsOutsideLocalInitializers()
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

            fn consume(allocator: ScratchAllocator) -> bool {
                return allocator.state != null;
            }

            fn forward(arena: Arena) -> ScratchAllocator {
                return arena;
            }

            fn main() -> int {
                let arena: Arena = Arena { used: 0 };
                let allocator: ScratchAllocator = arena;
                allocator = arena;
                return consume(allocator) && forward(arena).state != null ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.True(
            result.Output!.Split(
                ".vtable = &Arena_ScratchAllocator_vtable",
                StringSplitOptions.None).Length >= 4);
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
        Assert.Contains(".state = &arena", result.Output);
        Assert.Contains(
            ".vtable = &Arena_ScratchAllocator_vtable",
            result.Output);
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
