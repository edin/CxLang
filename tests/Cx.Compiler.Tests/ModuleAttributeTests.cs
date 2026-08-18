using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ModuleAttributeTests
{
    [Fact]
    public void Parse_FileModule_PreservesAttributes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @namespace("Demo")
            module demo;
            """);

        var module = Assert.IsType<ModuleDeclarationNode>(
            Assert.Single(program.Declarations));
        var attribute = Assert.Single(module.Attributes);
        Assert.Equal("namespace", attribute.Name);
        Assert.Equal("\"Demo\"", Assert.Single(attribute.Arguments).Value.ToSourceText());
    }

    [Fact]
    public void Parse_ModuleBlock_PreservesAttributes()
    {
        var program = CompilerTestHelpers.Parse(
            """
            @namespace("Demo")
            module demo {
                fn answer() -> int { return 42; }
            }
            """);

        var module = Assert.Single(program.ModuleBlocks);
        Assert.Equal("namespace", Assert.Single(module.Attributes).Name);
    }

    [Fact]
    public void Compile_ValidatesModuleAttributeTargetAndArguments()
    {
        CompilerTestHelpers.VerifyCompilationFiles(
            """
            // file: metadata.cx
            module demo;

            attribute namespace on module {
                value: string;
            }

            // file: main.cx
            @namespace(42)
            module demo;

            fn main() -> int { return 0; }
            """)
            .FailsWith(
                "Attribute 'namespace' argument 'value' expects metadata type 'string'");
    }

    [Fact]
    public void Compile_RejectsRepeatedAttributeAcrossModuleFiles()
    {
        CompilerTestHelpers.VerifyCompilationFiles(
            """
            // file: metadata.cx
            module demo;

            attribute namespace on module {
                value: string;
            }

            // file: first.cx
            @namespace("Demo")
            module demo;

            fn main() -> int { return 0; }

            // file: second.cx
            @namespace("Other")
            module demo;
            """)
            .FailsWith("Attribute 'namespace' cannot be applied more than once");
    }

    [Fact]
    public void Compile_MacroReadsMergedModuleAttribute()
    {
        CompilerTestHelpers.VerifyCompilationFiles(
            """
            // file: metadata.cx
            module demo;

            attribute namespace on module {
                value: string;
            }

            macro GenerateNamespace(target: module) -> declarations {
                fn generated_namespace() -> const char* {
                    return @{target.attribute("namespace").value};
                }

                fn generated_attribute_count() -> int {
                    return @{target.attributes.count};
                }
            }

            // file: api.cx
            @namespace("Demo")
            module demo;

            // file: main.cx
            module demo;

            use GenerateNamespace(module("demo"));

            fn main() -> int {
                return generated_namespace()[0] == 'D'
                    && generated_attribute_count() == 1
                    ? 0
                    : 1;
            }
            """)
            .Succeeds()
            .OutputContains("return \"Demo\"", "return 1")
            .OutputOmits("@namespace");
    }
}
