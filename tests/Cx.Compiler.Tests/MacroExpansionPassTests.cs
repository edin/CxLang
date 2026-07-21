using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class MacroExpansionPassTests
{
    [Fact]
    public void ExpandProgram_ExpandsReflectionDrivenStatementMacro()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                id: int;
                age: int;
            }

            macro debug(target: type, value: expression) -> statements {
                @foreach(field in fields(target)) {
                    debug_field(@{name(field)}, @{value}.@{name(field)});
                }
            }

            fn sample() -> int {
                use debug(User, user);
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(3, body.Count);
        Assert.Equal("debug_field(\"id\", user.id)", Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.Equal("debug_field(\"age\", user.age)", Assert.IsType<CStatement>(body[1]).Expression.ToSourceText());
        var origin = Assert.IsType<GeneratedSyntaxOrigin>(body[0].GeneratedFrom);
        Assert.Equal("use debug(User, user);", origin.InvocationSpan.Text);
        Assert.Contains("debug_field", Assert.IsType<Cx.Compiler.Source.SourceSpan>(origin.TemplateSpan).Text, StringComparison.Ordinal);
        Assert.IsType<ReturnStatement>(body[2]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_ReportsUnknownMacroAndWrongArity()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro trace(value: expression) -> statements {
                emit(@{value});
            }

            fn sample() -> int {
                use missing(1);
                use trace();
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Unknown macro 'missing'", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("expects 1 argument(s), but received 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_StopsRecursiveExpansionAtDepthLimit()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro recurse() -> statements {
                use recurse();
            }

            fn sample() -> int {
                use recurse();
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("maximum depth of 64", StringComparison.Ordinal));
    }


    [Fact]
    public void CompileToC_SupportsEndToEndUserWrittenDebugMacro()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn printf(format: const char*, ...) -> int;

            struct User {
                id: int;
                age: int;
            }

            macro debug(target: type, value: expression) -> statements {
                @foreach(field in fields(target)) {
                    printf("%s=%d\\n", @{name(field)}, @{value}.@{name(field)});
                }
            }

            fn main() -> int {
                let user: User = User { id: 7, age: 42 };
                use debug(User, user);
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("\"id\"", result.Output!, StringComparison.Ordinal);
        Assert.Contains("user.id", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"age\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("user.age", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DeclarationMacroGeneratesTypedFunctionAst()
    {
        var result = CompilerTestHelpers.Compile(
            """
            extern fn printf(format: const char*, ...) -> int;

            attribute debug_skip on field;

            struct User {
                id: int;
                age: int;

                @debug_skip
                secret: int;
            }

            macro generate_debug(target: type) -> declarations {
                fn debug_generated(value: @{target}) -> int {
                    @foreach(field in fields(target)) {
                        @if(!has_attribute(field, "debug_skip")) {
                            printf("%s=%d\\n", @{name(field)}, value.@{name(field)});
                        }
                    }

                    return 0;
                }
            }

            use generate_debug(User);

            fn main() -> int {
                let user: User = User { id: 7, age: 42, secret: 99 };
                return debug_generated(user);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("debug_generated", result.Output!, StringComparison.Ordinal);
        Assert.Contains("value.id", result.Output, StringComparison.Ordinal);
        Assert.Contains("value.age", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("value.secret", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandProgram_HoistsStructLocalMacroExtensionToModuleScope()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                id: int;
                use Debug(Self);
            }

            macro Debug(target: type) -> declarations {
                extension @{target} {
                    fn debug() -> int {
                        return self.id;
                    }
                }
            }
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var structNode = Assert.Single(expanded.Structs);
        Assert.Empty(structNode.MacroInvocationNodes);

        var extension = Assert.Single(expanded.Extensions);
        Assert.Equal("User", extension.TargetTypeNode!.ToSourceText());
        Assert.Equal("User", Assert.Single(extension.Methods).OwnerTypeNode!.ToSourceText());
        var origin = Assert.IsType<GeneratedSyntaxOrigin>(extension.GeneratedFrom);
        Assert.Equal("use Debug(Self);", origin.InvocationSpan.Text);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompileToC_DebugExtensionMacroWorksInsideOrOutsideStruct(bool invokeInsideStruct)
    {
        var source =
            """
            extern fn printf(format: const char*, ...) -> int;

            struct User {
                id: int;
                age: int;
                $INSIDE$
            }

            macro Debug(target: type) -> declarations {
                extension @{target} {
                    fn debug() -> int {
                        @foreach(field in fields(target)) {
                            @let field_name = field.name;
                            printf("%s=%d\\n", @{field_name}, self.@{field_name});
                        }

                        return 0;
                    }
                }
            }

            $OUTSIDE$

            fn main() -> int {
                let user: User = User { id: 7, age: 42 };
                return user.debug();
            }
            """
            .Replace("$INSIDE$", invokeInsideStruct ? "use Debug(Self);" : string.Empty, StringComparison.Ordinal)
            .Replace("$OUTSIDE$", invokeInsideStruct ? string.Empty : "use Debug(User);", StringComparison.Ordinal);

        var result = CompilerTestHelpers.Compile(source);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("debug", result.Output!, StringComparison.Ordinal);
        Assert.Contains("self->id", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->age", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DebugWriterMacroSupportsPrimitiveAndNestedStructFields()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*, output: StringBuilder*) -> bool;
            }

            extension int {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_int(*self);
                }
            }

            extension bool {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_bool(*self);
                }
            }

            extension double {
                fn write_debug(output: StringBuilder*) -> bool {
                    return output.append_double(*self);
                }
            }

            struct Address {
                number: int;

                use Debug(Self);
            }

            struct User {
                id: int;
                active: bool;
                score: double;
                address: Address;

                use Debug(Self);
            }

            macro Debug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn write_debug(output: StringBuilder*) -> bool {
                        if (!output.append_cstr(@{concat(target.name, " { ")})) {
                            return false;
                        }

                        let first: bool = true;
                        @foreach(field in target.fields) {
                            @let field_name = field.name;
                            if (!first && !output.append_cstr(", ")) {
                                return false;
                            }
                            first = false;

                            if (!output.append_cstr(@{concat(field.name, ": ")})) {
                                return false;
                            }

                            @let debug_match = requirement_match(field.type, Debug);
                            @if(!debug_match.success) {
                                @let _ = compile_error(concat(
                                    "Debug cannot generate '",
                                    target.name,
                                    ".",
                                    field.name,
                                    "': unsupported field type '",
                                    field.type.display_name,
                                    "'."));
                            }
                            @if(debug_match.success) {
                                if (!self.@{field_name}.write_debug(output)) {
                                    return false;
                                }
                            }
                        }

                        return output.append_cstr(" }");
                    }
                }
            }

            fn main() -> int {
                let builder: StringBuilder = StringBuilder.with_capacity((usize)32);
                let user: User = User {
                    id: 7,
                    active: true,
                    score: 3.5,
                    address: Address { number: 42 },
                };
                let ok: bool = user.write_debug(&builder);
                builder.free();
                return ok ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("self->id", result.Output!, StringComparison.Ordinal);
        Assert.Contains("self->active", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->score", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->address", result.Output, StringComparison.Ordinal);
        Assert.Contains("self->number", result.Output, StringComparison.Ordinal);
        Assert.Contains("append_int", result.Output!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DebugMacroRejectsUnsupportedFieldType()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*) -> bool;
            }

            struct User {
                count: usize;
                use Debug(Self);
            }

            macro Debug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn write_debug() -> bool {
                        @foreach(field in target.fields) {
                            @let debug_match = requirement_match(field.type, Debug);
                            @if(!debug_match.success) {
                                @let _ = compile_error(concat(
                                    "Debug cannot generate '",
                                    target.name,
                                    ".",
                                    field.name,
                                    "': unsupported field type '",
                                    field.type.display_name,
                                    "'."));
                            }
                        }

                        return true;
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Debug cannot generate 'User.count': unsupported field type 'usize'.");
    }

    [Fact]
    public void CompileToC_RejectsUnfulfilledMacroProvidedRequirement()
    {
        var result = CompilerTestHelpers.Compile(
            """
            requires Debug {
                fn write_debug(self: Self*) -> bool;
            }

            struct User {
                use BrokenDebug(Self);
            }

            macro BrokenDebug(target: type) -> declarations
                provides target: Debug {
                extension @{target} {
                    fn unrelated() -> bool {
                        return true;
                    }
                }
            }

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Macro 'BrokenDebug' claims that 'User' provides 'Debug'",
            "Missing function 'write_debug'");
    }

    [Fact]
    public void CompileToC_DeclarationArgumentGeneratesTypedFunctionWrapper()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            macro Wrap(function: declaration) -> declarations {
                fn @{as_name(concat("wrap_", function.name))}(@{function.parameters}) -> @{function.return_type} {
                    return @{as_name(function.name)}(@{function.parameters});
                }
            }

            use Wrap(add);

            fn main() -> int {
                return wrap_add(2, 5) == 7 ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("wrap_add", result.Output!, StringComparison.Ordinal);
        Assert.Contains("add(left, right)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_ParameterConstructorBuildsGeneratedFunctionSignature()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Parameter {
                value: int;
            }

            macro GenerateAdd() -> declarations {
                fn generated_add(@{[
                    Parameter.create("left", int),
                    Parameter.create("right", int)
                ]}) -> int {
                    return left + right;
                }
            }

            use GenerateAdd();

            fn main() -> int {
                return generated_add(2, 5) == 7 ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("generated_add(int left, int right)", result.Output!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DeclarationScriptBuildsAndMutatesParameterList()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            macro WrapWithContext(function: declaration) -> declarations {
                @let parameters = [];
                parameters.add(Parameter.create("context", int));
                @foreach(parameter in function.parameters) {
                    parameters.add(parameter);
                }

                fn @{as_name(concat("wrap_", function.name))}(@{parameters}) -> @{function.return_type} {
                    return @{as_name(function.name)}(@{function.parameters});
                }
            }

            use WrapWithContext(add);

            fn main() -> int {
                return wrap_add(0, 2, 5) == 7 ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("wrap_add(int context, int left, int right)", result.Output!, StringComparison.Ordinal);
        Assert.Contains("add(left, right)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DeclarationForeachAndIfGenerateOneFunctionPerMatchingMethod()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct Source {
                public static fn first() -> int {
                    return 1;
                }

                static fn hidden() -> int {
                    return 2;
                }

                public static fn second() -> int {
                    return 3;
                }
            }

            macro WrapPublic(target: type) -> declarations {
                @foreach(method in target.methods) {
                    @if(method.is_public) {
                        fn @{as_name(concat("wrap_", method.name))}() -> int {
                            return 0;
                        }
                    }
                }
            }

            use WrapPublic(Source);

            fn main() -> int {
                return wrap_first() + wrap_second();
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("wrap_first", result.Output!, StringComparison.Ordinal);
        Assert.Contains("wrap_second", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("wrap_hidden", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_ReportsDuplicateFunctionGeneratedByForeachAtInvocation()
    {
        var result = CompilerTestHelpers.Compile(
            """
            macro Generate() -> declarations {
                @foreach(name in ["same", "same"]) {
                    fn @{as_name(name)}() -> int {
                        return 0;
                    }
                }
            }

            use Generate();

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Macro-generated function 'same()' conflicts with macro-generated function");
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Macro-generated function 'same()'", StringComparison.Ordinal));
        Assert.Equal(9, diagnostic.Location.Line);
    }

    [Fact]
    public void CompileToC_AllowsGeneratedFunctionOverloadsWithDifferentParameterTypes()
    {
        var result = CompilerTestHelpers.Compile(
            """
            macro Generate(target: type) -> declarations {
                fn generated(value: @{target}) -> int {
                    return 0;
                }
            }

            use Generate(int);
            use Generate(usize);

            fn main() -> int {
                return generated(1);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
    }

    [Fact]
    public void CompileToC_ReportsGeneratedFunctionCollidingWithExistingDeclaration()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn existing(value: int) -> int {
                return value;
            }

            macro Generate() -> declarations {
                fn existing(value: int) -> int {
                    return value;
                }
            }

            use Generate();

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Macro-generated function 'existing(int)' conflicts with function declared at main.cx:1:1");
    }

    [Fact]
    public void CompileToC_ReportsGeneratedExtensionMethodCollidingWithOwnedMethod()
    {
        var result = CompilerTestHelpers.Compile(
            """
            struct User {
                fn debug() -> int {
                    return 1;
                }
            }

            macro Debug(target: type) -> declarations {
                extension @{target} {
                    fn debug() -> int {
                        return 2;
                    }
                }
            }

            use Debug(User);

            fn main() -> int {
                return 0;
            }
            """);

        CompilerTestHelpers.AssertDiagnosticContains(
            result,
            "Macro-generated function 'User.debug()' conflicts with function");
    }

    [Fact]
    public void CompileToC_ModuleReflectionEnumeratesOnlyPublicFunctionsAndTypes()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module sample;

            public fn exported() -> int {
                return 1;
            }

            fn hidden() -> int {
                return 2;
            }

            public struct User {}
            struct Hidden {}

            macro GenerateBindings() -> declarations {
                @foreach(function in module.public_functions) {
                    fn @{as_name(concat("wrap_", function.name))}() -> int {
                        return 0;
                    }
                }

                @let reflected_module = module("sample");
                @foreach(target in reflected_module.public_types) {
                    @if(target.is_struct) {
                        fn @{as_name(concat("bind_", target.name))}() -> int {
                            return 0;
                        }
                    }
                }
            }

            use GenerateBindings();

            fn main() -> int {
                return wrap_exported() + bind_User();
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("wrap_exported", result.Output!, StringComparison.Ordinal);
        Assert.Contains("bind_User", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("wrap_hidden", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("bind_Hidden", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_RouteMacroUsesNullableAttributeAndDynamicFields()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module api;

            attribute route on fn {
                method: string;
                path: string;
            }

            extern fn register_route(
                method: const char*,
                path: const char*,
                handler: fn() -> int
            ) -> void;

            @route(method: "GET", path: "/users")
            public fn users() -> int {
                return 0;
            }

            public fn health() -> int {
                return 0;
            }

            macro RegisterRoutes() -> declarations {
                fn register_routes() -> void {
                    @foreach(handler in module.public_functions) {
                        @let route = handler.attribute("route");
                        @if(route != null) {
                            @if(!handler.match(Type.from(fn() -> int))) {
                                compile_error(concat("Invalid route handler: ", handler.name));
                            }

                            register_route(
                                @{route.method},
                                @{route.path},
                                @{as_name(handler.name)}
                            );
                        }
                    }
                }
            }

            use RegisterRoutes();

            fn main() -> int {
                register_routes();
                return 0;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("register_route(\"GET\", \"/users\",", result.Output!, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/health\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_RouteMacroGeneratesDispatcherBody()
    {
        var result = CompilerTestHelpers.Compile(
            """
            module api;

            attribute route on fn {
                id: int;
            }

            @route(id: 1)
            public fn users() -> int {
                return 10;
            }

            @route(id: 2)
            public fn health() -> int {
                return 20;
            }

            public fn helper() -> int {
                return 30;
            }

            macro BuildDispatcher(target: module) -> declarations {
                fn dispatch(route_id: int) -> int {
                    @foreach(handler in target.public_functions) {
                        @let route = handler.attribute("route");
                        @if(route != null) {
                            if (route_id == @{route.id}) {
                                return @{handler.reference}();
                            }
                        }
                    }

                    return -1;
                }
            }

            use BuildDispatcher("api");

            fn main() -> int {
                return dispatch(2);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("route_id == 1", result.Output!, StringComparison.Ordinal);
        Assert.Contains("route_id == 2", result.Output, StringComparison.Ordinal);
        Assert.Contains("return users()", result.Output, StringComparison.Ordinal);
        Assert.Contains("return health()", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("return helper()", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_DispatchMacroUsesCrossModuleFunctionReferences()
    {
        var result = CompilerTestHelpers.Compile(
        [
            CompilerTestHelpers.Source(
                """
                module app;

                import api;

                fn users() -> int {
                    return -1;
                }

                macro BuildDispatcher(target: module) -> declarations {
                    fn dispatch(route_id: int) -> int {
                        @foreach(handler in target.public_functions) {
                            @let route = handler.attribute("route");
                            @if(route != null) {
                                if (route_id == @{route.id}) {
                                    return @{handler.reference}();
                                }
                            }
                        }

                        return -1;
                    }
                }

                use BuildDispatcher(module("api"));

                fn main() -> int {
                    return dispatch(1);
                }
                """),
            CompilerTestHelpers.Source(
                """
                module api;

                attribute route on fn {
                    id: int;
                }

                @route(id: 1)
                public fn users() -> int {
                    return 10;
                }
                """,
                "api.cx"),
        ]);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("return api_users()", result.Output!, StringComparison.Ordinal);
        Assert.DoesNotContain("return app_users()", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandProgram_ResolvedGenericMethodParametersCanBeSpliced()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Box<T> {
                static fn identity(value: T) -> T {
                    return value;
                }
            }

            macro Wrap(target: type) -> declarations {
                @let parameters = [];
                @foreach(method in target.methods) {
                    @foreach(parameter in method.parameters) {
                        parameters.add(parameter);
                    }
                }

                fn wrap_identity(@{parameters}) -> int {
                    return value;
                }
            }

            use Wrap(Box<int>);
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var wrapper = Assert.Single(expanded.Functions);
        var parameter = Assert.Single(wrapper.Parameters);
        Assert.Equal("value", parameter.Name);
        Assert.Equal("int", parameter.TypeNode?.ToSourceText());
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_BindsCompositeMacroTypeArgumentsStructurally()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {}

            macro EmitType(target: type) -> statements {
                emit(@{target.display_name});
            }

            fn sample() -> int {
                use EmitType(const User*);
                use EmitType(int[4]);
                use EmitType(fn(int) -> bool);
                return 0;
            }
            """);
        var diagnostics = new DiagnosticBag();

        var expanded = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal("emit(\"const User*\")", Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.Equal("emit(\"int[4]\")", Assert.IsType<CStatement>(body[1]).Expression.ToSourceText());
        Assert.Equal("emit(\"fn(int)->bool\")", Assert.IsType<CStatement>(body[2]).Expression.ToSourceText());
        Assert.IsType<ReturnStatement>(body[3]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void CompileToC_ConstructedAttributeCanBeAppliedToConstructedParameter()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute binding_name on parameter {
                value: string;
            }

            macro Generate() -> declarations {
                @let attributes = [
                    Attribute.create("binding_name", [
                        AttributeArgument.named("value", "native_context")
                    ])
                ];
                @let parameters = [
                    Parameter.create("context", int, attributes)
                ];

                fn generated(@{parameters}) -> int {
                    return context;
                }
            }

            use Generate();

            fn main() -> int {
                return generated(0);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("generated(int context)", result.Output!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_ReflectedParametersCanBeRenamedAndRetypedImmutably()
    {
        var result = CompilerTestHelpers.Compile(
            """
            fn add(left: int, right: int) -> int {
                return left + right;
            }

            macro Transform(function: declaration) -> declarations {
                @let parameters = [];
                @foreach(parameter in function.parameters) {
                    parameters.add(
                        parameter
                            .with_name(as_name(concat("wrapped_", parameter.name)))
                            .with_type(usize)
                    );
                }

                fn transformed(@{parameters}) -> usize {
                    return wrapped_left + wrapped_right;
                }
            }

            use Transform(add);

            fn main() -> int {
                return transformed(2, 5) == 7 ? 0 : 1;
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("transformed", result.Output!, StringComparison.Ordinal);
        Assert.Contains("wrapped_left", result.Output, StringComparison.Ordinal);
        Assert.Contains("wrapped_right", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileToC_ReflectedParameterAttributesCanBeExtendedImmutably()
    {
        var result = CompilerTestHelpers.Compile(
            """
            attribute source_parameter on parameter;
            attribute generated_parameter on parameter;

            fn source(@source_parameter value: int) -> int {
                return value;
            }

            macro Transform(function: declaration) -> declarations {
                @let parameters = [];
                @foreach(parameter in function.parameters) {
                    parameters.add(
                        parameter.add_attribute(
                            Attribute.create("generated_parameter")
                        )
                    );
                }

                fn transformed(@{parameters}) -> int {
                    return value;
                }
            }

            use Transform(source);

            fn main() -> int {
                return transformed(0);
            }
            """);

        CompilerTestHelpers.AssertSuccess(result);
        Assert.Contains("transformed(int value)", result.Output!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandProgram_RejectsRuntimeStatementInDeclarationScriptPosition()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Broken() -> declarations {
                missing.call();

                fn generated() -> int {
                    return 0;
                }
            }

            use Broken();
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "must evaluate entirely at compile time",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_ReportsInvalidComputedFunctionParameters()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Generate(value: expression) -> declarations {
                fn generated(@{value}) -> int {
                    return 0;
                }
            }

            use Generate(1);
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "must evaluate to a list of parameter declarations",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1", "must evaluate to a name or string")]
    [InlineData("\"not valid\"", "is not a valid identifier")]
    public void ExpandProgram_ReportsInvalidComputedFunctionName(string expression, string expectedDiagnostic)
    {
        var program = CompilerTestHelpers.Parse(
            $$"""
            macro Generate() -> declarations {
                fn @{{{expression}}}() -> int {
                    return 0;
                }
            }

            use Generate();
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(expectedDiagnostic, StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_ReportsInvalidUnknownAndAmbiguousDeclarationArguments()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn duplicate() -> int {
                return 1;
            }

            extern fn duplicate() -> int;

            macro Inspect(item: declaration) -> declarations {
            }

            use Inspect(1);
            use Inspect(missing);
            use Inspect(duplicate);
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "expects a named function declaration argument",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "could not resolve function declaration 'missing'",
                StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "found 2 function declarations named 'duplicate'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_ReportsInvalidModuleArgument()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro Inspect(target: module) -> declarations {
            }

            use Inspect(1);
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "expects a module argument or module-name string",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_BindsResolvedModuleArgument()
    {
        var program = CompilerTestHelpers.Parse(
            """
            module sample;

            macro Inspect(target: module) -> declarations {
                @foreach(function in target.public_functions) {
                }
            }

            use Inspect(module("sample"));
            """);
        var diagnostics = new DiagnosticBag();

        _ = new MacroExpansionPass(diagnostics, program).RewriteProgram(program);

        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }
}
