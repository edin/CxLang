using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeEvaluationContext
{
    private readonly CompileTimeEvaluationContext? _parent;
    private readonly Dictionary<string, CompileTimeValue> _bindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deferredBindings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readOnlyBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeNode> _declaredTypes = new(StringComparer.Ordinal);

    public CompileTimeEvaluationContext(CompileTimeEvaluationContext? parent = null)
    {
        _parent = parent;
    }

    public CompileTimeEvaluationContext CreateChild() => new(this);

    public void DefineDeferred(string name)
    {
        if (!_bindings.ContainsKey(name))
        {
            _deferredBindings.Add(name);
        }
    }

    public bool Define(
        string name,
        CompileTimeValue value,
        bool isMutable = true,
        TypeNode? declaredType = null)
    {
        if (!_bindings.TryAdd(name, value))
        {
            return false;
        }

        if (!isMutable)
        {
            _readOnlyBindings.Add(name);
        }
        if (declaredType is not null)
        {
            _declaredTypes.Add(name, declaredType);
        }

        return true;
    }

    public bool Assign(string name, CompileTimeValue value)
    {
        if (_bindings.ContainsKey(name))
        {
            if (_readOnlyBindings.Contains(name))
            {
                return false;
            }

            _bindings[name] = value;
            return true;
        }

        return _parent?.Assign(name, value) ?? false;
    }

    public bool IsReadOnly(string name)
    {
        if (_bindings.ContainsKey(name))
        {
            return _readOnlyBindings.Contains(name);
        }

        return _parent?.IsReadOnly(name) ?? false;
    }

    public bool TryGetDeclaredType(string name, out TypeNode type)
    {
        if (_declaredTypes.TryGetValue(name, out type!))
        {
            return true;
        }

        if (_bindings.ContainsKey(name))
        {
            type = null!;
            return false;
        }

        if (_parent is not null)
        {
            return _parent.TryGetDeclaredType(name, out type);
        }

        type = null!;
        return false;
    }

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

    public bool IsDeferred(string name)
    {
        if (_bindings.ContainsKey(name))
        {
            return false;
        }

        if (_deferredBindings.Contains(name))
        {
            return true;
        }

        return _parent?.IsDeferred(name) ?? false;
    }
}
