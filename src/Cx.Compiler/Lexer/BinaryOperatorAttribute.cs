namespace Cx.Compiler.Lexer;

[AttributeUsage(AttributeTargets.Field)]
public sealed class BinaryOperatorAttribute : Attribute
{
    public BinaryOperatorAttribute(
        int precedence,
        OperatorAssociativity associativity = OperatorAssociativity.Left)
    {
        Precedence = precedence;
        Associativity = associativity;
    }

    public int Precedence { get; }

    public OperatorAssociativity Associativity { get; }
}
