namespace Cx.Compiler.Syntax.Nodes;

public enum UnaryOperator
{
    Plus,
    Negate,
    LogicalNot,
    BitwiseNot,
    Dereference,
    AddressOf,
    Increment,
    Decrement,
}

public enum PostfixOperator
{
    Increment,
    Decrement,
}

public enum BinaryOperator
{
    Multiply,
    Divide,
    Modulo,
    Add,
    Subtract,
    ShiftLeft,
    ShiftRight,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Compare,
    Equal,
    NotEqual,
    BitwiseAnd,
    BitwiseXor,
    BitwiseOr,
    LogicalAnd,
    LogicalOr,
}

public enum OperatorKind
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
}

public enum AssignmentOperator
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
}

public static class ExpressionOperatorFacts
{
    public static string ToSourceText(this OperatorKind op) => op switch
    {
        OperatorKind.Add => "+",
        OperatorKind.Subtract => "-",
        OperatorKind.Multiply => "*",
        OperatorKind.Divide => "/",
        OperatorKind.Modulo => "%",
        _ => throw Unsupported(op),
    };

    public static string ToFunctionName(this OperatorKind op) => op switch
    {
        OperatorKind.Add => "operator_add",
        OperatorKind.Subtract => "operator_subtract",
        OperatorKind.Multiply => "operator_multiply",
        OperatorKind.Divide => "operator_divide",
        OperatorKind.Modulo => "operator_modulo",
        _ => throw Unsupported(op),
    };

    public static OperatorKind? ToOverloadableOperator(this BinaryOperator op) => op switch
    {
        BinaryOperator.Add => OperatorKind.Add,
        BinaryOperator.Subtract => OperatorKind.Subtract,
        BinaryOperator.Multiply => OperatorKind.Multiply,
        BinaryOperator.Divide => OperatorKind.Divide,
        BinaryOperator.Modulo => OperatorKind.Modulo,
        _ => null,
    };

    public static string ToSourceText(this UnaryOperator op) => op switch
    {
        UnaryOperator.Plus => "+",
        UnaryOperator.Negate => "-",
        UnaryOperator.LogicalNot => "!",
        UnaryOperator.BitwiseNot => "~",
        UnaryOperator.Dereference => "*",
        UnaryOperator.AddressOf => "&",
        UnaryOperator.Increment => "++",
        UnaryOperator.Decrement => "--",
        _ => throw Unsupported(op),
    };

    public static string ToSourceText(this PostfixOperator op) => op switch
    {
        PostfixOperator.Increment => "++",
        PostfixOperator.Decrement => "--",
        _ => throw Unsupported(op),
    };

    public static string ToSourceText(this BinaryOperator op) => op switch
    {
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Modulo => "%",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.ShiftLeft => "<<",
        BinaryOperator.ShiftRight => ">>",
        BinaryOperator.LessThan => "<",
        BinaryOperator.LessThanOrEqual => "<=",
        BinaryOperator.GreaterThan => ">",
        BinaryOperator.GreaterThanOrEqual => ">=",
        BinaryOperator.Compare => "<=>",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.BitwiseAnd => "&",
        BinaryOperator.BitwiseXor => "^",
        BinaryOperator.BitwiseOr => "|",
        BinaryOperator.LogicalAnd => "&&",
        BinaryOperator.LogicalOr => "||",
        _ => throw Unsupported(op),
    };

    public static string ToSourceText(this AssignmentOperator op) => op switch
    {
        AssignmentOperator.Assign => "=",
        AssignmentOperator.Add => "+=",
        AssignmentOperator.Subtract => "-=",
        AssignmentOperator.Multiply => "*=",
        AssignmentOperator.Divide => "/=",
        AssignmentOperator.Modulo => "%=",
        _ => throw Unsupported(op),
    };

    private static InvalidOperationException Unsupported<T>(T op) where T : struct, Enum =>
        new($"Unsupported {typeof(T).Name} value '{op}'.");
}
