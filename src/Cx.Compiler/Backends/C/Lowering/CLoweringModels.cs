namespace Cx.Compiler;

internal sealed record ExpressionLoweringServices(
    CExpressionLoweringPipeline Pipeline,
    MemberAccessLowerer MemberAccessLowerer,
    MemberCallLowerer MemberCallLowerer,
    CoreDirectCallLowerer CoreDirectCallLowerer);
