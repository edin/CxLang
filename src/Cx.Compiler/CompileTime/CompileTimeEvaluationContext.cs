namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeEvaluationContext
{
    private readonly CompileTimeEvaluationContext? _parent;
    private readonly Dictionary<string, CompileTimeValue> _bindings = new(StringComparer.Ordinal);

    public CompileTimeEvaluationContext(CompileTimeEvaluationContext? parent = null)
    {
        _parent = parent;
    }

    public CompileTimeEvaluationContext CreateChild() => new(this);

    public bool Define(string name, CompileTimeValue value) =>
        _bindings.TryAdd(name, value);

    public bool TryGet(string name, out CompileTimeValue value)
    {
        if (_bindings.TryGetValue(name, out value!))
        {
            return true;
        }

        if (_parent is not null)
        {
            return _parent.TryGet(name, out value);
        }

        value = null!;
        return false;
    }
}
