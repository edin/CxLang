namespace Cx.Compiler.CompileTime;

internal abstract class CompileTimeTypeBinding
{
    public virtual string? GlobalName => null;

    public virtual string? ScriptTypeName => null;

    public abstract Type ReceiverType { get; }

    public virtual bool AcceptsScriptValue(CompileTimeValue value)
    {
        object receiver = value is CompileTimeValue.Syntax syntax
            ? syntax.Value
            : value;
        return ReceiverType.IsInstanceOfType(receiver);
    }

    public virtual CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        new CompileTimePropertyResult.Missing();
}
