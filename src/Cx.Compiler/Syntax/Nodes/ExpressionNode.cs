using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public abstract record ExpressionNode(Location Location) : SyntaxNode(Location)
{
    [Cx.Compiler.LegacyStringType("Compatibility text rebuilt from expression nodes. Prefer node-specific properties or ToSourceText().")]
    public virtual string SourceText => this.ToSourceText();
}

public sealed record RawExpressionNode(
    Location Location,
    string RawText) : ExpressionNode(Location)
{
    public override string SourceText => RawText;
}

public sealed record LiteralExpressionNode(
    Location Location,
    string LiteralText) : ExpressionNode(Location)
{
    public override string SourceText => LiteralText;
}

public sealed record NameExpressionNode(
    Location Location,
    string Name) : ExpressionNode(Location)
{
    public override string SourceText => Name;
}

public sealed record ParenthesizedExpressionNode(
    Location Location,
    ExpressionNode Expression) : ExpressionNode(Location);

public sealed record CastExpressionNode(
    Location Location,
    ExpressionNode Expression,
    TypeNode? TargetTypeNode = null) : ExpressionNode(Location);

public sealed record UnaryExpressionNode(
    Location Location,
    string Operator,
    ExpressionNode Operand) : ExpressionNode(Location);

public sealed record PostfixExpressionNode(
    Location Location,
    ExpressionNode Operand,
    string Operator) : ExpressionNode(Location);

public abstract record SizeOfOperandNode(Location Location);

public sealed record SizeOfTypeOperandNode(
    Location Location,
    TypeNode TypeNode) : SizeOfOperandNode(Location);

public sealed record SizeOfExpressionOperandNode(
    Location Location,
    ExpressionNode Expression) : SizeOfOperandNode(Location);

public sealed record SizeOfUnresolvedOperandNode(
    Location Location,
    string FallbackText,
    ExpressionNode? ExpressionCandidate = null) : SizeOfOperandNode(Location);

public sealed record SizeOfExpressionNode : ExpressionNode
{
    public SizeOfExpressionNode(
        Location Location,
        ExpressionNode? ExpressionOperand,
        TypeNode? TypeOperandNode = null,
        SizeOfOperandNode? OperandNode = null)
        : base(Location)
    {
        this.ExpressionOperand = ExpressionOperand;
        this.TypeOperandNode = TypeOperandNode;
        this.OperandNode = OperandNode
            ?? CreateOperand(Location, ExpressionOperand, TypeOperandNode);
    }

    public ExpressionNode? ExpressionOperand { get; set; }

    public TypeNode? TypeOperandNode { get; set; }

    public SizeOfOperandNode OperandNode { get; set; }

    private static SizeOfOperandNode CreateOperand(
        Location location,
        ExpressionNode? expressionOperand,
        TypeNode? typeOperandNode) =>
        typeOperandNode is not null
            ? new SizeOfTypeOperandNode(typeOperandNode.Location, typeOperandNode)
            : expressionOperand is not null
                ? new SizeOfExpressionOperandNode(expressionOperand.Location, expressionOperand)
                : new SizeOfUnresolvedOperandNode(location, string.Empty);
}

public sealed record BinaryExpressionNode(
    Location Location,
    ExpressionNode Left,
    string Operator,
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
    string Operator,
    ExpressionNode Value) : ExpressionNode(Location);

public sealed record CallExpressionNode(
    Location Location,
    ExpressionNode Callee,
    IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode(Location);

public sealed record GenericCallExpressionNode(
    Location Location,
    ExpressionNode Callee,
    IReadOnlyList<ExpressionNode> Arguments,
    IReadOnlyList<TypeNode> TypeArgumentNodes) : ExpressionNode(Location)
{
    public GenericCallExpressionNode(
        Location Location,
        ExpressionNode Callee,
        IReadOnlyList<string> TypeArguments,
        IReadOnlyList<ExpressionNode> Arguments)
        : this(
            Location,
            Callee,
            Arguments,
            TypeArguments.Select(type => TypeNode.CreateFromText(Location, type)).ToList())
    {
    }
}

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
        RawExpressionNode raw => raw.RawText,
        LiteralExpressionNode literal => literal.LiteralText,
        NameExpressionNode name => name.Name,
        ParenthesizedExpressionNode parenthesized => $"({parenthesized.Expression.ToSourceText()})",
        CastExpressionNode cast => $"({cast.TargetTypeNode.ToTypeName()}){cast.Expression.ToSourceText()}",
        UnaryExpressionNode unary => unary.Operator + unary.Operand.ToSourceText(),
        PostfixExpressionNode postfix => postfix.Operand.ToSourceText() + postfix.Operator,
        SizeOfExpressionNode sizeOf => $"sizeof({sizeOf.OperandNode.ToSourceText()})",
        BinaryExpressionNode binary => $"{binary.Left.ToSourceText()} {binary.Operator} {binary.Right.ToSourceText()}",
        ConditionalExpressionNode conditional =>
            $"{conditional.Condition.ToSourceText()} ? {conditional.WhenTrue.ToSourceText()} : {conditional.WhenFalse.ToSourceText()}",
        ScalarRangeExpressionNode range =>
            $"{range.Start.ToSourceText()}{(range.IsInclusive ? "..." : "..")}{range.End.ToSourceText()}",
        InitializerExpressionNode initializer => FormatInitializer(initializer),
        FunctionExpressionNode function => FormatFunctionExpression(function),
        AssignmentExpressionNode assignment =>
            $"{assignment.Target.ToSourceText()} {assignment.Operator} {assignment.Value.ToSourceText()}",
        GenericCallExpressionNode call => FormatGenericCall(call),
        CallExpressionNode call => FormatCall(call),
        MemberExpressionNode member => $"{member.Target.ToSourceText()}.{member.MemberName}",
        IndexExpressionNode index => $"{index.Target.ToSourceText()}[{index.Index.ToSourceText()}]",
        _ => expression.SourceText,
    };

    public static string ToSourceText(this SizeOfOperandNode operand) => operand switch
    {
        SizeOfTypeOperandNode type => type.TypeNode.ToTypeName(),
        SizeOfExpressionOperandNode expression => expression.Expression.ToSourceText(),
        SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } unresolved => unresolved.ExpressionCandidate.ToSourceText(),
        SizeOfUnresolvedOperandNode unresolved => unresolved.FallbackText,
        _ => string.Empty,
    };

    private static string FormatInitializer(InitializerExpressionNode initializer)
    {
        var fields = initializer.Fields.Select(field => $"{field.Name}: {field.Value.ToSourceText()}");
        var values = initializer.Values.Select(value => value.ToSourceText());
        var prefix = initializer.TypeNameNode.ToTypeName();
        return prefix + "{" + string.Join(", ", fields.Concat(values)) + "}";
    }

    private static string FormatFunctionExpression(FunctionExpressionNode function)
    {
        var parameters = string.Join(", ", function.Parameters.Select(FormatParameter));
        var returnType = function.ReturnTypeNode is null ? string.Empty : " -> " + function.ReturnTypeNode.ToTypeName();
        if (function.ExpressionBody is not null)
        {
            return $"fn({parameters}){returnType} => {function.ExpressionBody.ToSourceText()}";
        }

        return $"fn({parameters}){returnType} {{...}}";
    }

    private static string FormatParameter(ParameterNode parameter) =>
        parameter.Name + (parameter.TypeNode is null ? string.Empty : ": " + parameter.TypeNode.ToTypeName());

    private static string FormatGenericCall(GenericCallExpressionNode call)
    {
        var typeArguments = string.Join(", ", call.TypeArgumentNodes.Select(type => type.ToTypeName()));
        return $"{call.Callee.ToSourceText()}<{typeArguments}>({FormatArguments(call.Arguments)})";
    }

    private static string FormatCall(CallExpressionNode call) =>
        $"{call.Callee.ToSourceText()}({FormatArguments(call.Arguments)})";

    private static string FormatArguments(IEnumerable<ExpressionNode> arguments) =>
        string.Join(", ", arguments.Select(argument => argument.ToSourceText()));
}
