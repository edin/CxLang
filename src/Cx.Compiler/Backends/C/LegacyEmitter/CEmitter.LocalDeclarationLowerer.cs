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
            var initializer = let.Initializer is null
                ? null
                : nameLowerer.LowerInitializerExpression(let.TypeNode?.Semantic.Type, type, let.Initializer);
            return new CLocalDeclarationStatement(
                LowerVariable(let.TypeNode, type, let.Name, let.IsConst, nameLowerer.SelfType),
                initializer);
        }

        public CLocalDeclarationStatement LowerForDeclaration(ForDeclarationInitializerNode declaration) =>
            new(
                LowerVariable(
                    declaration.TypeNode,
                    ForDeclarationInitializerTypeText(declaration),
                    declaration.Name,
                    declaration.IsConst,
                    nameLowerer.SelfType),
                declaration.Initializer is null
                    ? null
                    : nameLowerer.LowerInitializerExpression(
                        declaration.TypeNode?.Semantic.Type,
                        ForDeclarationInitializerTypeText(declaration),
                        declaration.Initializer));

        public CForInitializerNode LowerForInitializer(ForInitializerNode initializer) => initializer switch
        {
            ForDeclarationInitializerNode declaration => new CDeclarationForInitializer(
                LowerVariable(
                    declaration.TypeNode,
                    ForDeclarationInitializerTypeText(declaration),
                    declaration.Name,
                    declaration.IsConst,
                    nameLowerer.SelfType),
                declaration.Initializer is null
                    ? null
                    : nameLowerer.LowerInitializerExpression(
                        declaration.TypeNode?.Semantic.Type,
                        ForDeclarationInitializerTypeText(declaration),
                        declaration.Initializer)),
            ForExpressionInitializerNode expression => new CExpressionForInitializer(
                nameLowerer.LowerExpression(expression.Expression)),
            _ => new CEmptyForInitializer(),
        };
    }
}
