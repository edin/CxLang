namespace Cx.Compiler.Tests;

public sealed class SemanticInterfaceBindingTypeSystemTests
{
    [Fact]
    public void Compile_DeclaresInterfaceBeforeStructContainingInterfacePointer()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputAppearsInOrder(
                "typedef struct AllocatorHandle",
                "} Buffer;");
    }

    [Fact]
    public void Compile_AllowsStructToInterfaceBindingThroughTypeSystem()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                ".state = &arena",
                ".vtable = &Arena_ScratchAllocator_vtable",
                ".allocate =",
                "Arena_allocate");
    }

    [Fact]
    public void Compile_LowersInterfaceConversionsOutsideLocalInitializers()
    {
        var test = CompilerTestHelpers.VerifyCompilation(
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
            """)
            .Succeeds();
        Assert.True(
            test.Result.Output!.Split(
                ".vtable = &Arena_ScratchAllocator_vtable",
                StringSplitOptions.None).Length >= 4);
    }

    [Fact]
    public void Compile_AllowsAliasSourceToInterfaceBindingThroughTypeSystem()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .OutputContains(
                ".state = &arena",
                ".vtable = &Arena_ScratchAllocator_vtable");
    }

    [Fact]
    public void Compile_ReportsStructThatDoesNotImplementInterface()
    {
        CompilerTestHelpers.VerifyCompilation(
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
            """)
            .FailsWith(
                "Type mismatch for local 'allocator'",
                "cannot assign 'Arena' to 'ScratchAllocator'");
    }
}
