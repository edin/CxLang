using Cx.Compiler.Diagnostics;
using Cx.Compiler.CompileTime;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class CxPostSemanticLoweringPipeline(DiagnosticBag diagnostics)
{
    public ProgramNode Lower(
        ProgramNode program,
        FunctionCatalog? functionCatalog = null,
        CompileTimeEnvironment? compileTimeEnvironment = null,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
    {
        if (diagnostics.HasErrors)
        {
            return program;
        }

        var lowered = LambdaLowerer.Lower(program, diagnostics);
        lowered = RangeForeachLowerer.Lower(lowered, diagnostics);
        lowered = DataEnumForeachLowerer.Lower(lowered, diagnostics);
        lowered = IteratorForeachLowerer.Lower(lowered, diagnostics);
        lowered = ContiguousForeachLowerer.Lower(lowered, diagnostics);
        lowered = MatchLoweringPass.Lower(lowered, diagnostics);
        var specializationDirectiveExpansion =
            new CompileTimeDirectiveExpansionPass(
                diagnostics,
                new ProgramCompileTimeReflection(
                    lowered,
                    moduleNamesByPath),
                compileTimeEnvironment);
        lowered = GenericSpecializationPass.Apply(
            lowered,
            diagnostics,
            functionCatalog,
            specialization => ExpandSpecializationDirectives(
                specialization,
                specializationDirectiveExpansion));
        lowered = DataEnumDefaultMaterializationPass.Apply(lowered);
        CoreCxFunctionAnnotationPass.Apply(lowered);
        CoreCxReferenceAnnotationPass.AnnotateLinkedDeclarations(lowered);
        CoreCxCallNormalizationPass.Apply(lowered, functionCatalog);
        lowered = CoreCxOperatorDerivationLoweringPass.Apply(lowered);
        CoreCxCallAnnotationPass.Apply(lowered);
        CoreCxReferenceAnnotationPass.Apply(lowered);
        CoreCxMemberAccessAnnotationPass.Apply(lowered);
        CoreCxInterfaceAnnotationPass.Apply(lowered);
        new CoreCxValueConversionPass(lowered).Apply();
        new CoreCxValidator(diagnostics).Validate(lowered);
        return lowered;
    }

    private static FunctionNode ExpandSpecializationDirectives(
        FunctionNode specialization,
        CompileTimeDirectiveExpansionPass expansion)
    {
        var context = new CompileTimeEvaluationContext();
        if (specialization.Semantic.GenericFunctionSpecialization is { } generic)
        {
            foreach (var (name, type) in generic.Definition.TypeParameters
                .Zip(generic.TypeArguments))
            {
                context.Define(
                    name,
                    new CompileTimeValue.Type(type),
                    isMutable: false);
            }
        }

        var body = expansion.ExpandStatementList(
            specialization.Body,
            context);
        return SyntaxNode.CloneMetadata(
            specialization,
            specialization with { Body = body });
    }
}
