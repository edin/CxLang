using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal sealed class CLocalDeclarationLowerer(CBackendContext backend, ImportedNameLowerer nameLowerer)
{
    public CLocalDeclarationStatement LowerLet(LetStatement let)
    {
        return new CLocalDeclarationStatement(
            CDeclarationLowerer.LowerVariable(backend, let.TypeNode, let.Name, let.IsConst, nameLowerer.SelfTypeRef),
            LowerInitializer(let.TypeNode, let.Name, let.Initializer));
    }

    public CLocalDeclarationStatement LowerForDeclaration(ForDeclarationInitializerNode declaration) =>
        new(
            LowerForVariable(declaration),
            LowerInitializer(declaration.TypeNode, declaration.Name, declaration.Initializer));

    public CForInitializerNode LowerForInitializer(ForInitializerNode initializer) => initializer switch
    {
        ForDeclarationInitializerNode declaration => new CDeclarationForInitializer(
            LowerForVariable(declaration),
            LowerInitializer(declaration.TypeNode, declaration.Name, declaration.Initializer)),
        ForExpressionInitializerNode expression => new CExpressionForInitializer(
            nameLowerer.LowerExpression(expression.Expression)),
        _ => new CEmptyForInitializer(),
    };

    private CVariableDeclaration LowerForVariable(ForDeclarationInitializerNode declaration) =>
        CDeclarationLowerer.LowerVariable(
            backend,
            declaration.TypeNode,
            declaration.Name,
            declaration.IsConst,
            nameLowerer.SelfTypeRef);

    private CExpression? LowerInitializer(
        TypeNode? typeNode,
        string name,
        ExpressionNode? initializer) =>
        initializer is null
            ? null
            : nameLowerer.LowerInitializerExpression(
                CDeclarationLowerer.ResolveInitializerTargetType(typeNode, name),
                initializer);

}
