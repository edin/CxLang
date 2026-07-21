using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CompileTimeDirectiveExpansionPassTests
{
    [Fact]
    public void ExpandProgram_SelectsCompileTimeIfBranch()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                @if(enabled) {
                    return 1;
                } else {
                    return 2;
                }
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("enabled", new CompileTimeValue.Boolean(true));

        var (expanded, diagnostics) = Expand(program, context);

        Assert.Equal(
            "1",
            Assert.IsType<LiteralExpressionNode>(
                Assert.IsType<ReturnStatement>(
                    Assert.Single(Assert.Single(expanded.Functions).Body)).Expression).LiteralText);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_ExpandsForeachWithScopedBindingsAndNestedIf()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                @foreach(item in [1, 2, 3]) {
                    @if(item > 1) {
                        emit();
                    }
                }

                return 0;
            }
            """);

        var (expanded, diagnostics) = Expand(program);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(3, body.Count);
        Assert.IsType<CStatement>(body[0]);
        Assert.IsType<CStatement>(body[1]);
        Assert.IsType<ReturnStatement>(body[2]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_UsesReflectionInsideForeachContext()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                name: int;
                age: int;
            }

            fn main() -> int {
                @foreach(field in fields(target)) {
                    @if(name(field) == "age") {
                        emit_age();
                    }
                }

                return 0;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (expanded, diagnostics) = Expand(program, context);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(2, body.Count);
        Assert.Equal(
            "emit_age()",
            Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_UsesCompileTimeLetAndReflectionProperties()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                name: int;
                age: usize;
            }

            fn generated() -> int {
                @foreach(field in fields(target)) {
                    @let field_name = field.name;
                    @let field_type = field.type;
                    @if(field_type.kind == "named") {
                        emit(@{as_name(field_name)});
                    }
                }

                return 0;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (expanded, diagnostics) = Expand(program, context);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(3, body.Count);
        Assert.Equal("emit(name)", Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.Equal("emit(age)", Assert.IsType<CStatement>(body[1]).Expression.ToSourceText());
        Assert.IsType<ReturnStatement>(body[2]);
        Assert.DoesNotContain(body, statement => statement is CompileTimeLetStatementNode);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_DoesNotLeakCompileTimeLetOutOfBranchScope()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn generated() -> int {
                @if(true) {
                    @let branch_value = true;
                }

                @if(branch_value) {
                    emit();
                }

                return 0;
            }
            """);

        var (_, diagnostics) = Expand(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Unknown compile-time name 'branch_value'", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_UsesRequirementMatchBindingInGeneratedTypePosition()
    {
        var program = CompilerTestHelpers.Parse(
            """
            requires Contiguous<T> {
                data: T*;
                length: usize;
            }

            struct Buffer: Contiguous<int> {
                data: int*;
                length: usize;
            }

            fn generated() -> int {
                @let match = target.match(Contiguous);
                @if(match.success) {
                    let item: @{match.T} = 0;
                }

                return 0;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("Buffer", [])));

        var (expanded, diagnostics) = Expand(program, context);

        var body = Assert.Single(expanded.Functions).Body;
        var item = Assert.IsType<LetStatement>(body[0]);
        Assert.Equal("int", item.TypeNode?.ToSourceText());
        Assert.IsType<ReturnStatement>(body[1]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_SubstitutesExpressionMemberAndTypeSlotsInForeachContext()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                name: int;
                age: usize;
            }

            fn generated() -> int {
                @foreach(field in fields(target)) {
                    let copy: @{type(field)} = self.@{name(field)};
                    emit(@{as_name(name(field))});
                }

                return 0;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (expanded, diagnostics) = Expand(program, context);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(5, body.Count);
        var firstCopy = Assert.IsType<LetStatement>(body[0]);
        Assert.Equal("int", firstCopy.TypeNode?.ToSourceText());
        Assert.Equal("self.name", firstCopy.Initializer!.ToSourceText());
        Assert.Equal("emit(name)", Assert.IsType<CStatement>(body[1]).Expression.ToSourceText());
        var secondCopy = Assert.IsType<LetStatement>(body[2]);
        Assert.Equal("usize", secondCopy.TypeNode?.ToSourceText());
        Assert.Equal("self.age", secondCopy.Initializer!.ToSourceText());
        Assert.Equal("emit(age)", Assert.IsType<CStatement>(body[3]).Expression.ToSourceText());
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_ReportsWrongComputedSlotKinds()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn generated() -> int {
                let value: @{1} = self.@{true};
                return 0;
            }
            """);

        var (_, diagnostics) = Expand(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Computed type must evaluate to a type", StringComparison.Ordinal));
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Computed member name must evaluate to a name or string", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_ExpandsDirectivesInsideCDeclareMembers()
    {
        var program = CompilerTestHelpers.Parse(
            """
            declare "sample.h" {
                @foreach(library in ["first", "second"]) {
                    @if(library == "second") {
                        link "selected";
                    }
                }
            }
            """);

        var (expanded, diagnostics) = Expand(program);

        Assert.Equal("selected", Assert.Single(Assert.Single(expanded.CDeclarations).Links).Library);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_DoesNotEvaluateDirectivesInsideMacroDefinition()
    {
        var program = CompilerTestHelpers.Parse(
            """
            macro sample(enabled: expression) -> statements {
                @if(enabled) {
                    emit();
                }
            }
            """);

        var (expanded, diagnostics) = Expand(program);

        Assert.IsType<CompileTimeIfStatementNode>(
            Assert.Single(Assert.Single(expanded.Macros).Template.Statements));
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_ReportsNonBooleanIfCondition()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                @if(1) {
                    return 1;
                }

                return 0;
            }
            """);

        var (_, diagnostics) = Expand(program);

        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(
                "Compile-time @if condition must evaluate to a boolean value",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandProgram_ExecutesCompileTimeObjectMethodStatements()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn generated() -> int {
                @let values = [];
                values.add(1);
                @foreach(value in [2, 3]) {
                    values.add(value);
                }
                @if(values.count == 3) {
                    emit();
                }
                return 0;
            }
            """);

        var (expanded, diagnostics) = Expand(program);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(2, body.Count);
        Assert.Equal("emit()", Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.IsType<ReturnStatement>(body[1]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ExpandProgram_IteratesReflectedPublicMethods()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct User {
                public fn visible() -> int {
                    return 1;
                }

                fn hidden() -> int {
                    return 2;
                }
            }

            fn generated() -> int {
                @foreach(method in target.methods) {
                    @if(method.is_public) {
                        emit_public_method();
                    }
                }

                return 0;
            }
            """);
        var context = new CompileTimeEvaluationContext();
        context.Define("target", new CompileTimeValue.Type(new TypeRef.Named("User", [])));

        var (expanded, diagnostics) = Expand(program, context);

        var body = Assert.Single(expanded.Functions).Body;
        Assert.Equal(2, body.Count);
        Assert.Equal(
            "emit_public_method()",
            Assert.IsType<CStatement>(body[0]).Expression.ToSourceText());
        Assert.IsType<ReturnStatement>(body[1]);
        CompilerTestHelpers.AssertNoErrors(diagnostics);
    }

    private static (ProgramNode Program, DiagnosticBag Diagnostics) Expand(
        ProgramNode program,
        CompileTimeEvaluationContext? context = null)
    {
        var diagnostics = new DiagnosticBag();
        var pass = new CompileTimeDirectiveExpansionPass(
            diagnostics,
            new ProgramCompileTimeReflection(program));
        return (pass.ExpandProgram(program, context), diagnostics);
    }
}
