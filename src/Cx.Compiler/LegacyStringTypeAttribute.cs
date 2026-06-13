namespace Cx.Compiler;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Method)]
internal sealed class LegacyStringTypeAttribute(string note) : Attribute
{
    public string Note { get; } = note;
}
