using Cx.Compiler.C;

namespace Cx.Compiler.Tests;

public sealed class ReceiverExpressionBuilderTests
{
    [Theory]
    [InlineData(false, true, "&value")]
    [InlineData(true, true, "value")]
    [InlineData(false, false, "value")]
    [InlineData(true, false, "*value")]
    public void Build_AdaptsExpressionReceiverToSelfParameter(
        bool isPointer,
        bool takesPointerSelf,
        string expected)
    {
        var expression = ReceiverExpressionBuilder.Build(
            new CNameExpression("value"),
            isPointer,
            takesPointerSelf);

        Assert.Equal(expected, new CExpressionEmitter().Emit(expression));
    }
}
