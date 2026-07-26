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
    Compare,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
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
        OperatorKind.Compare => "<=>",
        OperatorKind.Equal => "==",
        OperatorKind.NotEqual => "!=",
        OperatorKind.LessThan => "<",
        OperatorKind.LessThanOrEqual => "<=",
        OperatorKind.GreaterThan => ">",
        OperatorKind.GreaterThanOrEqual => ">=",
        _ => throw Unsupported(op),
    };

    public static string ToFunctionName(this OperatorKind op) => op switch
    {
        OperatorKind.Add => "operator_add",
        OperatorKind.Subtract => "operator_subtract",
        OperatorKind.Multiply => "operator_multiply",
        OperatorKind.Divide => "operator_divide",
        OperatorKind.Modulo => "operator_modulo",
        OperatorKind.Compare => "operator_compare",
        OperatorKind.Equal => "operator_equal",
        OperatorKind.NotEqual => "operator_not_equal",
        OperatorKind.LessThan => "operator_less_than",
        OperatorKind.LessThanOrEqual => "operator_less_than_or_equal",
        OperatorKind.GreaterThan => "operator_greater_than",
        OperatorKind.GreaterThanOrEqual => "operator_greater_than_or_equal",
        _ => throw Unsupported(op),
    };

    public static OperatorKind? ToOverloadableOperator(this BinaryOperator op) => op switch
    {
        BinaryOperator.Add => OperatorKind.Add,
        BinaryOperator.Subtract => OperatorKind.Subtract,
        BinaryOperator.Multiply => OperatorKind.Multiply,
        BinaryOperator.Divide => OperatorKind.Divide,
        BinaryOperator.Modulo => OperatorKind.Modulo,
        BinaryOperator.Compare => OperatorKind.Compare,
        BinaryOperator.Equal => OperatorKind.Equal,
        BinaryOperator.NotEqual => OperatorKind.NotEqual,
        BinaryOperator.LessThan => OperatorKind.LessThan,
        BinaryOperator.LessThanOrEqual => OperatorKind.LessThanOrEqual,
        BinaryOperator.GreaterThan => OperatorKind.GreaterThan,
        BinaryOperator.GreaterThanOrEqual => OperatorKind.GreaterThanOrEqual,
        _ => null,
    };

    public static BinaryOperator ToBinaryOperator(this OperatorKind op) => op switch
    {
        OperatorKind.Add => BinaryOperator.Add,
        OperatorKind.Subtract => BinaryOperator.Subtract,
        OperatorKind.Multiply => BinaryOperator.Multiply,
        OperatorKind.Divide => BinaryOperator.Divide,
        OperatorKind.Modulo => BinaryOperator.Modulo,
        OperatorKind.Compare => BinaryOperator.Compare,
        OperatorKind.Equal => BinaryOperator.Equal,
        OperatorKind.NotEqual => BinaryOperator.NotEqual,
        OperatorKind.LessThan => BinaryOperator.LessThan,
        OperatorKind.LessThanOrEqual => BinaryOperator.LessThanOrEqual,
        OperatorKind.GreaterThan => BinaryOperator.GreaterThan,
        OperatorKind.GreaterThanOrEqual => BinaryOperator.GreaterThanOrEqual,
        _ => throw Unsupported(op),
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
