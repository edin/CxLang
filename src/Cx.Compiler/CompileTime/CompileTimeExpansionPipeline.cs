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
        foreach (var list in AstExpressionTraversal
            .Enumerate(program)
            .OfType<ListExpressionNode>())
        {
            diagnostics.Report(
                list.Location,
                "List expressions are only valid during compile-time evaluation.");
        }

        foreach (var typeLiteral in AstExpressionTraversal
            .Enumerate(program)
            .OfType<TypeLiteralExpressionNode>())
        {
            diagnostics.Report(
                typeLiteral.Location,
                "Type literals are only valid during compile-time evaluation.");
        }

        if (!validateIncompleteMembers)
        {
            return;
        }

        foreach (var incompleteMember in AstExpressionTraversal
            .Enumerate(program)
            .OfType<IncompleteMemberExpressionNode>())
        {
            diagnostics.Report(
                incompleteMember.DotSpan,
                "Expected member name after '.'.");
        }
    }
}
