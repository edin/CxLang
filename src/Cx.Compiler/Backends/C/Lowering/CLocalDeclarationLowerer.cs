using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CLocalDeclarationLowerer(CBackendContext backend, ImportedNameLowerer nameLowerer)
{
    public CLocalDeclarationStatement LowerLet(LetStatement let)
    {
        var type = CEmitter.LetStatementTypeText(let);
        return new CLocalDeclarationStatement(
            CEmitter.LowerVariable(backend, let.TypeNode, type, let.Name, let.IsConst, nameLowerer.SelfType),
            LowerInitializer(let.TypeNode, type, let.Name, let.Initializer));
    }

    public CLocalDeclarationStatement LowerForDeclaration(ForDeclarationInitializerNode declaration) =>
        new(
            LowerForVariable(declaration),
            LowerInitializer(
                declaration.TypeNode,
                CEmitter.ForDeclarationInitializerTypeText(declaration),
                declaration.Name,
                declaration.Initializer));

    public CForInitializerNode LowerForInitializer(ForInitializerNode initializer) => initializer switch
    {
        ForDeclarationInitializerNode declaration => new CDeclarationForInitializer(
            LowerForVariable(declaration),
            LowerInitializer(
                declaration.TypeNode,
                CEmitter.ForDeclarationInitializerTypeText(declaration),
                declaration.Name,
                declaration.Initializer)),
        ForExpressionInitializerNode expression => new CExpressionForInitializer(
            nameLowerer.LowerExpression(expression.Expression)),
        _ => new CEmptyForInitializer(),
    };

    private CVariableDeclaration LowerForVariable(ForDeclarationInitializerNode declaration) =>
        CEmitter.LowerVariable(
            backend,
            declaration.TypeNode,
            CEmitter.ForDeclarationInitializerTypeText(declaration),
            declaration.Name,
            declaration.IsConst,
            nameLowerer.SelfType);

    private CExpression? LowerInitializer(
        TypeNode? typeNode,
        string fallbackType,
        string name,
        ExpressionNode? initializer) =>
        initializer is null
            ? null
            : nameLowerer.LowerInitializerExpression(
                CEmitter.ResolveInitializerTargetType(typeNode, fallbackType, name),
                initializer);
}
