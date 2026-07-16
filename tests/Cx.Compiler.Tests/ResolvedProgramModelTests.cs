using Cx.Compiler.C;
using Cx.Compiler.Semantic;

namespace Cx.Compiler.Tests;

public sealed class ResolvedProgramModelTests
{
    [Fact]
    public void Build_ResolvesConcreteGenericTypeAndOwnedMethods()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                data: T*;

                static fn create() -> Vec<T> {
                }

                fn push(value: T) -> void {
                }
            }

            extension Vec<T> {
                fn count() -> usize {
                }
            }
            """);

        var model = ResolvedProgramModel.Build(program);

        Assert.True(model.TryGetType(VecOfInt(), out var vec));
        Assert.Equal(ResolvedTypeKind.Struct, vec.Kind);
        Assert.Equal("Vec_int", vec.CName);
        Assert.Contains(vec.Methods, method => method.SourceName == "push");
        Assert.Contains(vec.Methods, method => method.SourceName == "count");
        Assert.Contains(vec.StaticMethods, method => method.SourceName == "create");
    }

    [Fact]
    public void Build_ResolvesEnumsAndCDeclarations()
    {
        var program = CompilerTestHelpers.Parse(
            """
            enum Color {
                Red,
                Blue
            }

            declare <x.h> {
                type Handle = opaque;
                fn make() -> Handle;
            }
            """);

        var model = ResolvedProgramModel.Build(program);

        Assert.True(model.TryGetType(new TypeRef.Named("Color", []), out var color));
        Assert.Equal(ResolvedTypeKind.Enum, color.Kind);
        Assert.Equal(["Red", "Blue"], color.EnumMembers.Select(member => member.CName));

        Assert.True(model.TryGetType(new TypeRef.Named("Handle", []), out var handle));
        Assert.Equal(ResolvedTypeKind.ExternalAlias, handle.Kind);

        var make = Assert.Single(model.ExternFunctions);
        Assert.Equal("make", make.CName);
        Assert.True(make.IsExtern);
    }

    [Fact]
    public void Build_UsesModulePrefixOptionForFunctionCNames()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module std.core;

            fn helper() -> void {
            }
            """);
        Assert.Single(program.Functions).Semantic.ModuleName = "std.core";

        var model = ResolvedProgramModel.Build(program, new CNameManglerOptions(UseModulePrefixes: true));

        Assert.Equal("std_core_helper", Assert.Single(model.Functions).CName);
    }

    [Fact]
    public void Build_DistinguishesTypesWithSameNameFromDifferentModules()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Vec<T> {
                value: T;
            }

            struct Vec<T> {
                value: T;
            }
            """);
        program.Structs[0].Semantic.ModuleName = "std.core";
        program.Structs[1].Semantic.ModuleName = "app.collections";

        var model = ResolvedProgramModel.Build(program);

        Assert.True(model.TryGetType(
            new TypeRef.Named("Vec", [new TypeRef.Named("int", [])], ModuleName: "std.core"),
            out var stdVec));
        Assert.True(model.TryGetType(
            new TypeRef.Named("Vec", [new TypeRef.Named("int", [])], ModuleName: "app.collections"),
            out var appVec));

        Assert.Equal("std.core", Assert.IsType<TypeRef.Named>(stdVec.Type).ModuleName);
        Assert.Equal("app.collections", Assert.IsType<TypeRef.Named>(appVec.Type).ModuleName);
        Assert.NotEqual(TypeIdentity.ResolvedKey(stdVec.Type), TypeIdentity.ResolvedKey(appVec.Type));
    }

    private static TypeRef VecOfInt() =>
        new TypeRef.Named("Vec", [new TypeRef.Named("int", [])]);
}
