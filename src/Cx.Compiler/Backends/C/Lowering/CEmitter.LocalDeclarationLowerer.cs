using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private sealed class CLocalDeclarationLowerer(ImportedNameLowerer nameLowerer)
    {
        public CLocalDeclarationStatement LowerLet(LetStatement let)
        {
            var type = LetStatementTypeText(let);
            return new CLocalDeclarationStatement(
                LowerVariable(let.TypeNode, type, let.Name, let.IsConst, nameLowerer.SelfType),
                LowerInitializer(let.TypeNode, type, let.Initializer));
        }

        public CLocalDeclarationStatement LowerForDeclaration(ForDeclarationInitializerNode declaration) =>
            new(
                LowerForVariable(declaration),
                LowerInitializer(
                    declaration.TypeNode,
                    ForDeclarationInitializerTypeText(declaration),
                    declaration.Initializer));

        public CForInitializerNode LowerForInitializer(ForInitializerNode initializer) => initializer switch
        {
            ForDeclarationInitializerNode declaration => new CDeclarationForInitializer(
                LowerForVariable(declaration),
                LowerInitializer(
                    declaration.TypeNode,
                    ForDeclarationInitializerTypeText(declaration),
                    declaration.Initializer)),
            ForExpressionInitializerNode expression => new CExpressionForInitializer(
                nameLowerer.LowerExpression(expression.Expression)),
            _ => new CEmptyForInitializer(),
        };

        private CVariableDeclaration LowerForVariable(ForDeclarationInitializerNode declaration) =>
            LowerVariable(
                declaration.TypeNode,
                ForDeclarationInitializerTypeText(declaration),
                declaration.Name,
                declaration.IsConst,
                nameLowerer.SelfType);

        private CExpression? LowerInitializer(TypeNode? typeNode, string fallbackType, ExpressionNode? initializer) =>
            initializer is null
                ? null
                : typeNode?.Semantic.Type is { } typeRef
                    ? nameLowerer.LowerInitializerExpression(typeRef, initializer)
                    : nameLowerer.LowerInitializerExpression(fallbackType, initializer);
    }
}
