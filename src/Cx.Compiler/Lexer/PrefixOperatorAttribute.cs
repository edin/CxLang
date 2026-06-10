namespace Cx.Compiler.Lexer;

[AttributeUsage(AttributeTargets.Field)]
public sealed class PrefixOperatorAttribute : Attribute
{
    public PrefixOperatorAttribute(int precedence)
    {
        Precedence = precedence;
    }

    public int Precedence { get; }
}
