using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.C;

internal sealed class CInitializerExpressionLowerer(
    ICExpressionLoweringContext context,
    CTypeExpressionLowerer typeExpressionLowerer)
{
    public CExpression LowerInitializer(InitializerExpressionNode initializer) =>
        new CInitializerExpression(
            initializer.TypeNameNode is not null ? typeExpressionLowerer.LowerType(initializer.TypeNameNode) : null,
            initializer.Fields
                .Select(field => new CInitializerField(field.Name, context.LowerExpression(field.Value)))
                .ToList(),
            initializer.Values.Select(context.LowerExpression).ToList());
}
