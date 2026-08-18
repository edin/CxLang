namespace Cx.Compiler.Tests;

public sealed class ProgramReflectionTests
{
    [Fact]
    public void Compile_ModuleReflectsCategorizedDeclarationsAndLookups()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app {
                let internal_global: int = 1;
                public const exported_global: int = 2;

                compile const internal_constant: int = 3;
                public compile const exported_constant: int = 4;

                interface InternalService {}
                public interface Service {
                    fn run(value: int) -> int;
                }

                requires InternalRequirement {}
                public requires Resettable {
                    fn reset(self: Self*) -> void;
                }

                macro Inspect() -> declarations {
                    @let reflected = program.module("app");
                    @if(reflected.globals.count != 2 || reflected.public_globals.count != 1) {
                        compile_error("unexpected reflected globals");
                    }
                    @if(reflected.constants.count != 2 || reflected.public_constants.count != 1) {
                        compile_error("unexpected reflected constants");
                    }
                    @if(reflected.interfaces.count != 2 || reflected.public_interfaces.count != 1) {
                        compile_error("unexpected reflected interfaces");
                    }
                    @if(reflected.requirements.count != 2 || reflected.public_requirements.count != 1) {
                        compile_error("unexpected reflected requirements");
                    }
                    @if(reflected.global("exported_global").name != "exported_global") {
                        compile_error("global lookup failed");
                    }
                    @if(reflected.constant("exported_constant").name != "exported_constant") {
                        compile_error("constant lookup failed");
                    }
                    @if(reflected.interface("Service").methods[0].name != "run") {
                        compile_error("interface lookup failed");
                    }
                    @if(reflected.requirement("Resettable").members.count != 1) {
                        compile_error("requirement lookup failed");
                    }

                    fn reflection_succeeded() -> bool { return true; }
                }

                use Inspect();

                fn main() -> int { return reflection_succeeded() ? 0 : 1; }
            }
            """)
            .Succeeds()
            .OutputContains("reflection_succeeded");
    }

    [Fact]
    public void Compile_ModuleDeclarationLookupReportsMissingKindAndName()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app {
                macro Inspect() -> declarations {
                    @let missing = program.module("app").constant("missing");
                }

                use Inspect();
                fn main() -> int { return 0; }
            }
            """)
            .FailsWith(
                "Compile-time module 'app' does not contain constant 'missing'.");
    }

    [Fact]
    public void Compile_ModuleReflectsVisibleAttributeDeclarationsAndSchemas()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app {
                import metadata;

                macro Inspect() -> declarations {
                    @let reflected = program.module("metadata");
                    @if(reflected.attribute_declarations.count != 2
                        || reflected.public_attribute_declarations.count != 1) {
                        compile_error("unexpected reflected attribute declarations");
                    }

                    @let schema = reflected.attribute_declaration("export");
                    @if(schema.name != "export" || !schema.is_public) {
                        compile_error("attribute declaration lookup failed");
                    }
                    @if(schema.targets[0] != "function") {
                        compile_error("attribute declaration targets were not reflected");
                    }
                    @if(schema.fields[0].name != "name" || schema.fields[0].type != "string") {
                        compile_error("attribute declaration fields were not reflected");
                    }

                    fn schema_reflection_succeeded() -> bool { return true; }
                }

                use Inspect();
                fn main() -> int { return schema_reflection_succeeded() ? 0 : 1; }
            }

            module metadata {
                attribute internal on function {}

                public attribute export on function {
                    name: string;
                }
            }
            """)
            .Succeeds()
            .OutputContains("schema_reflection_succeeded");
    }

    [Fact]
    public void Compile_MacroEnumeratesProgramModulesAndReadsModuleMetadata()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app {
                import api;

                macro Generate() -> declarations {
                    @let found_api = false;
                    @foreach candidate in program.modules {
                        @if(candidate.name == "api") {
                            found_api = true;
                        }
                    }

                    @if(!found_api) {
                        compile_error("program.modules did not contain api");
                    }

                    @let api_module = program.module("api");
                    fn generated_namespace() -> const char* {
                        return @{api_module.attribute("namespace").value};
                    }
                }

                use Generate();

                fn main() -> int {
                    return generated_namespace()[0] == 'D' ? 0 : 1;
                }
            }

            @namespace("Demo")
            module api {
                public attribute namespace on module {
                    value: string;
                }

                public fn answer() -> int { return 42; }
            }
            """)
            .Succeeds()
            .OutputContains("return \"Demo\"")
            .OutputOmits("program.modules", "program.module");
    }

    [Fact]
    public void Compile_ProgramModuleRejectsModuleOutsideImportGraph()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            module app {
                macro Inspect() -> declarations {
                    @let hidden = program.module("hidden");
                }

                use Inspect();

                fn main() -> int { return 0; }
            }

            module hidden {
                public fn value() -> int { return 42; }
            }
            """)
            .FailsWith(
                "Compile-time program does not contain visible module 'hidden'");
    }

    [Fact]
    public void Compile_ProgramModuleReportsUnknownModule()
    {
        CompilerTestHelpers.VerifyCompilation(
            """
            macro Inspect() -> declarations {
                @let missing = program.module("missing");
            }

            use Inspect();

            fn main() -> int { return 0; }
            """)
            .FailsWith(
                "Compile-time program does not contain visible module 'missing'");
    }
}
