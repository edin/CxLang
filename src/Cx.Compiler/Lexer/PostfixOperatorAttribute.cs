namespace Cx.Compiler.Lexer;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PostfixOperatorAttribute : Attribute
{
    public PostfixOperatorAttribute(int precedence)
    {
        Precedence = precedence;
    }

    public int Precedence { get; }
}
