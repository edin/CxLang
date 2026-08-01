using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeExpansionPipeline(
    DiagnosticBag diagnostics,
    CompilationProfiler profiler,
    IReadOnlyDictionary<string, string> moduleNamesByPath)
{
    public ProgramNode? Expand(
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

        var macroExpansion = new MacroExpansionPass(
            diagnostics,
            program,
            new ProgramCompileTimeReflection(program, moduleNamesByPath),
            moduleNamesByPath);
        program = profiler.Measure(
            "Macro expansion",
            () => macroExpansion.RewriteProgram(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        var directiveExpansion = new CompileTimeDirectiveExpansionPass(
            diagnostics,
            new ProgramCompileTimeReflection(program, moduleNamesByPath));
        program = profiler.Measure(
            "Compile-time directive expansion",
            () => directiveExpansion.ExpandProgram(program));
        if (diagnostics.HasErrors)
        {
            return null;
        }

        profiler.Measure(
            "Compile-time residue validation",
            () => ValidateResidue(program, validateIncompleteMembers));
        return diagnostics.HasErrors
            ? null
            : program;
    }

    private void ValidateResidue(
        ProgramNode program,
        bool validateIncompleteMembers)
    {
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
}
