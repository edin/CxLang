using Cx.Compiler.C;

namespace Cx.Compiler;

internal sealed class ReceiverExpressionBuilder(CLoweringScope scope)
{
    public CExpression Build(string target, bool isPointer, bool takesPointerSelf)
    {
        if (scope.IsImplicitReferenceLocal(target))
        {
            return takesPointerSelf
                ? new CNameExpression(target)
                : new CUnaryExpression("*", new CNameExpression(target));
        }

        return takesPointerSelf
            ? isPointer
                ? new CNameExpression(target)
                : new CUnaryExpression("&", new CNameExpression(target))
            : new CNameExpression(target);
    }

    public static CExpression Build(CExpression target, bool isPointer, bool takesPointerSelf) =>
        takesPointerSelf
            ? isPointer
                ? target
                : new CUnaryExpression("&", target)
            : isPointer
                ? new CUnaryExpression("*", target)
                : target;
}
