namespace Cx.Compiler.CompileTime;

internal abstract class CompileTimeTypeBinding
{
    public virtual string? GlobalName => null;

    public abstract Type ReceiverType { get; }

    public virtual CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        new CompileTimePropertyResult.Missing();
}
