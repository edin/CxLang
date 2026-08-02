using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeExpansionResult(
    ProgramNode Program,
    CompileTimeEnvironment Environment);

internal sealed class CompileTimeExpansionPipeline(
    DiagnosticBag diagnostics,
    CompilationProfiler profiler,
    IReadOnlyDictionary<string, string> moduleNamesByPath,
    IReadOnlyList<ProgramNode> sourcePrograms)
{
    public CompileTimeExpansionResult? Expand(
        ProgramNode program,
        bool validateIncompleteMembers)
    {
        profiler.Measure(
            "Compile-time syntax block placement analysis",
            () => new CompileTimeSyntaxBlockPlacementAnalyzer(diagnostics)
                .Analyze(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        var environment = CompileTimeEnvironment.Create(
            program,
            sourcePrograms,
            moduleNamesByPath);
        environment.Functions.Validate(diagnostics);
        environment.Constants.Validate(diagnostics);
        if (diagnostics.HasErrors)
        {
            return null;
        }

        var directiveExpansion = new CompileTimeDirectiveExpansionPass(
            diagnostics,
            new ProgramCompileTimeReflection(program, moduleNamesByPath),
            environment: environment);
        program = profiler.Measure(
            "Compile-time directive expansion",
            () => directiveExpansion.ExpandProgram(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        var macroExpansion = new MacroExpansionPass(
            diagnostics,
            program,
            new ProgramCompileTimeReflection(program, moduleNamesByPath),
            moduleNamesByPath,
            environment: environment);
        program = profiler.Measure(
            "Macro expansion",
            () => macroExpansion.RewriteProgram(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        profiler.Measure(
            "Compile-time residue validation",
            () => ValidateResidue(program, validateIncompleteMembers));
        return diagnostics.HasErrors
            ? null
            : new CompileTimeExpansionResult(program, environment);
    }

    private void ValidateResidue(
        ProgramNode program,
        bool validateIncompleteMembers)
    {
        foreach (var type in program.Declarations
            .Where(declaration => declaration is not MacroDeclarationNode)
            .SelectMany(AstTraversal.DescendantsAndSelf<TypeNode>)
            .Where(type => ContainsNullableType(type.Syntax)))
        {
            diagnostics.Report(
                type.Location,
                $"Nullable runtime type '{type.ToSourceText()}' is not supported yet; 'T?' is currently limited to compile-time functions.");
        }

        foreach (var list in ExecutableAstTraversal
            .DescendantsAndSelf<ListExpressionNode>(program))
        {
            diagnostics.Report(
                list.Location,
                "List expressions are only valid during compile-time evaluation.");
        }

        foreach (var typeLiteral in ExecutableAstTraversal
            .DescendantsAndSelf<TypeLiteralExpressionNode>(program))
        {
            diagnostics.Report(
                typeLiteral.Location,
                "Type literals are only valid during compile-time evaluation.");
        }

        if (!validateIncompleteMembers)
        {
            return;
        }

        foreach (var incompleteMember in ExecutableAstTraversal
            .DescendantsAndSelf<IncompleteMemberExpressionNode>(program))
        {
            diagnostics.Report(
                incompleteMember.DotSpan,
                "Expected member name after '.'.");
        }
    }

    private static bool ContainsNullableType(TypeSyntaxNode syntax) =>
        syntax switch
        {
            NullableTypeSyntaxNode => true,
            GenericTypeSyntaxNode generic =>
                ContainsNullableType(generic.Target)
                || generic.Arguments.Any(ContainsNullableType),
            PointerTypeSyntaxNode pointer => ContainsNullableType(pointer.Element),
            ConstTypeSyntaxNode constant => ContainsNullableType(constant.Element),
            FixedArrayTypeSyntaxNode array => ContainsNullableType(array.Element),
            FunctionTypeSyntaxNode function =>
                function.Parameters.Any(ContainsNullableType)
                || ContainsNullableType(function.ReturnType),
            _ => false,
        };
}
