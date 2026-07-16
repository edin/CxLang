using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public abstract record ExpressionNode(Location Location) : SyntaxNode(Location);

public sealed record ErrorExpressionNode(Location Location) : ExpressionNode(Location);

public sealed record LiteralExpressionNode(
    Location Location,
    string LiteralText) : ExpressionNode(Location);

public sealed record NameExpressionNode(
    Location Location,
    string Name) : ExpressionNode(Location);

public sealed record ParenthesizedExpressionNode(
    Location Location,
    ExpressionNode Expression) : ExpressionNode(Location);

public sealed record CastExpressionNode(
    Location Location,
    ExpressionNode Expression,
    TypeNode? TargetTypeNode = null) : ExpressionNode(Location);

public sealed record UnaryExpressionNode(
    Location Location,
    UnaryOperator Operator,
    ExpressionNode Operand) : ExpressionNode(Location);

public sealed record PostfixExpressionNode(
    Location Location,
    ExpressionNode Operand,
    PostfixOperator Operator) : ExpressionNode(Location);

public abstract record SizeOfOperandNode(Location Location);

public sealed record SizeOfTypeOperandNode(
    Location Location,
    TypeNode TypeNode) : SizeOfOperandNode(Location);

public sealed record SizeOfExpressionOperandNode(
    Location Location,
    ExpressionNode Expression) : SizeOfOperandNode(Location);

public sealed record SizeOfUnresolvedOperandNode(
    Location Location,
    ExpressionNode? ExpressionCandidate = null) : SizeOfOperandNode(Location);

public sealed record SizeOfExpressionNode : ExpressionNode
{
    public SizeOfExpressionNode(
        Location location,
        SizeOfOperandNode operand)
        : base(location)
    {
        Operand = operand;
    }

    public SizeOfOperandNode Operand { get; set; }
}

public sealed record BinaryExpressionNode(
    Location Location,
    ExpressionNode Left,
    BinaryOperator Operator,
    ExpressionNode Right) : ExpressionNode(Location);

public sealed record ConditionalExpressionNode(
    Location Location,
    ExpressionNode Condition,
    ExpressionNode WhenTrue,
    ExpressionNode WhenFalse) : ExpressionNode(Location);

public sealed record ScalarRangeExpressionNode(
    Location Location,
    ExpressionNode Start,
    ExpressionNode End,
    bool IsInclusive) : ExpressionNode(Location);

public sealed record InitializerExpressionNode(
    Location Location,
    IReadOnlyList<InitializerFieldNode> Fields,
    IReadOnlyList<ExpressionNode> Values,
    TypeNode? TypeNameNode = null) : ExpressionNode(Location);

public sealed record InitializerFieldNode(
    string Name,
    ExpressionNode Value);

public sealed record FunctionExpressionNode(
    Location Location,
    IReadOnlyList<ParameterNode> Parameters,
    ExpressionNode? ExpressionBody,
    IReadOnlyList<StatementNode>? BlockBody,
    TypeNode? ReturnTypeNode = null) : ExpressionNode(Location);

public sealed record AssignmentExpressionNode(
    Location Location,
    ExpressionNode Target,
    AssignmentOperator Operator,
    ExpressionNode Value) : ExpressionNode(Location);

public sealed record CallExpressionNode(
    Location Location,
    ExpressionNode Callee,
    IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode(Location);

public sealed record GenericCallExpressionNode(
    Location Location,
    ExpressionNode Callee,
    IReadOnlyList<ExpressionNode> Arguments,
    IReadOnlyList<TypeNode> TypeArgumentNodes) : ExpressionNode(Location);

public sealed record MemberExpressionNode(
    Location Location,
    ExpressionNode Target,
    string MemberName) : ExpressionNode(Location);

public sealed record IndexExpressionNode(
    Location Location,
    ExpressionNode Target,
    ExpressionNode Index) : ExpressionNode(Location);

public static class ExpressionNodeExtensions
{
    public static string ToSourceText(this ExpressionNode expression) => expression switch
    {
        ErrorExpressionNode => "<error>",
        LiteralExpressionNode literal => literal.LiteralText,
        NameExpressionNode name => name.Name,
        ParenthesizedExpressionNode parenthesized => $"({parenthesized.Expression.ToSourceText()})",
        CastExpressionNode cast => $"({cast.TargetTypeNode.ToSourceText()}){cast.Expression.ToSourceText()}",
        UnaryExpressionNode unary => unary.Operator.ToSourceText() + unary.Operand.ToSourceText(),
        PostfixExpressionNode postfix => postfix.Operand.ToSourceText() + postfix.Operator.ToSourceText(),
        SizeOfExpressionNode sizeOf => $"sizeof({sizeOf.Operand.ToSourceText()})",
        BinaryExpressionNode binary => $"{binary.Left.ToSourceText()} {binary.Operator.ToSourceText()} {binary.Right.ToSourceText()}",
        ConditionalExpressionNode conditional =>
            $"{conditional.Condition.ToSourceText()} ? {conditional.WhenTrue.ToSourceText()} : {conditional.WhenFalse.ToSourceText()}",
        ScalarRangeExpressionNode range =>
            $"{range.Start.ToSourceText()}{(range.IsInclusive ? "..." : "..")}{range.End.ToSourceText()}",
        InitializerExpressionNode initializer => FormatInitializer(initializer),
        FunctionExpressionNode function => FormatFunctionExpression(function),
        AssignmentExpressionNode assignment =>
            $"{assignment.Target.ToSourceText()} {assignment.Operator.ToSourceText()} {assignment.Value.ToSourceText()}",
        GenericCallExpressionNode call => FormatGenericCall(call),
        CallExpressionNode call => FormatCall(call),
        MemberExpressionNode member => $"{member.Target.ToSourceText()}.{member.MemberName}",
        IndexExpressionNode index => $"{index.Target.ToSourceText()}[{index.Index.ToSourceText()}]",
        _ => throw new InvalidOperationException(
            $"No source formatter is registered for expression node '{expression.GetType().Name}'."),
    };

    public static string ToSourceText(this SizeOfOperandNode operand) => operand switch
    {
        SizeOfTypeOperandNode type => type.TypeNode.ToSourceText(),
        SizeOfExpressionOperandNode expression => expression.Expression.ToSourceText(),
        SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } unresolved => unresolved.ExpressionCandidate.ToSourceText(),
        SizeOfUnresolvedOperandNode => "<unresolved>",
        _ => string.Empty,
    };

    private static string FormatInitializer(InitializerExpressionNode initializer)
    {
        var fields = initializer.Fields.Select(field => $"{field.Name}: {field.Value.ToSourceText()}");
        var values = initializer.Values.Select(value => value.ToSourceText());
        var prefix = initializer.TypeNameNode.ToSourceText();
        return prefix + "{" + string.Join(", ", fields.Concat(values)) + "}";
    }

    private static string FormatFunctionExpression(FunctionExpressionNode function)
    {
        var parameters = string.Join(", ", function.Parameters.Select(FormatParameter));
        var returnType = function.ReturnTypeNode is null ? string.Empty : " -> " + function.ReturnTypeNode.ToSourceText();
        if (function.ExpressionBody is not null)
        {
            return $"fn({parameters}){returnType} => {function.ExpressionBody.ToSourceText()}";
        }

        return $"fn({parameters}){returnType} {{...}}";
    }

    private static string FormatParameter(ParameterNode parameter) =>
        parameter.Name + (parameter.TypeNode is null ? string.Empty : ": " + parameter.TypeNode.ToSourceText());

    private static string FormatGenericCall(GenericCallExpressionNode call)
    {
        var typeArguments = string.Join(", ", call.TypeArgumentNodes.Select(type => type.ToSourceText()));
        return $"{call.Callee.ToSourceText()}<{typeArguments}>({FormatArguments(call.Arguments)})";
    }

    private static string FormatCall(CallExpressionNode call) =>
        $"{call.Callee.ToSourceText()}({FormatArguments(call.Arguments)})";

    private static string FormatArguments(IEnumerable<ExpressionNode> arguments) =>
        string.Join(", ", arguments.Select(argument => argument.ToSourceText()));
}
