using System.Reflection;

namespace Cx.Compiler.CompileTime;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class CompileTimePropertyAttribute(string propertyName) : Attribute
{
    public string PropertyName { get; } = propertyName;
}

internal sealed class CompileTimePropertyRegistry
{
    private static readonly Lazy<CompileTimePropertyRegistry> DefaultRegistry = new(
        () => CreateFromBindings(BuiltInCompileTimeBindings.Bindings));

    private readonly IReadOnlyDictionary<(Type ReceiverType, string PropertyName), RegisteredProperty> _properties;
    private readonly IReadOnlyDictionary<Type, CompileTimeTypeBinding> _bindings;

    private CompileTimePropertyRegistry(
        IReadOnlyDictionary<(Type ReceiverType, string PropertyName), RegisteredProperty> properties,
        IReadOnlyDictionary<Type, CompileTimeTypeBinding> bindings)
    {
        _properties = properties;
        _bindings = bindings;
    }

    public static CompileTimePropertyRegistry Default => DefaultRegistry.Value;

    internal static CompileTimePropertyRegistry CreateFromBindings(
        params CompileTimeTypeBinding[] bindings) =>
        CreateFromBindings((IEnumerable<CompileTimeTypeBinding>)bindings);

    internal static CompileTimePropertyRegistry CreateFromBindings(
        IEnumerable<CompileTimeTypeBinding> bindings)
    {
        var properties = new Dictionary<(Type, string), RegisteredProperty>();
        var registeredBindings = new Dictionary<Type, CompileTimeTypeBinding>();
        foreach (var binding in bindings)
        {
            if (!registeredBindings.TryAdd(binding.ReceiverType, binding))
            {
                throw new InvalidOperationException(
                    $"Duplicate compile-time type binding for receiver type '{binding.ReceiverType.Name}'.");
            }

            var methods = binding.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var marker = method.GetCustomAttribute<CompileTimePropertyAttribute>();
                if (marker is null)
                {
                    continue;
                }

                var isLegacy = ValidateHandler(binding, method, marker);
                var key = (binding.ReceiverType, marker.PropertyName);
                if (properties.TryGetValue(key, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate compile-time property '{binding.ReceiverType.Name}.{marker.PropertyName}' is registered by '{existing.Method.DeclaringType?.FullName}.{existing.Method.Name}' and '{method.DeclaringType?.FullName}.{method.Name}'.");
                }

                properties.Add(key, new RegisteredProperty(binding, method, isLegacy));
            }
        }

        return new CompileTimePropertyRegistry(properties, registeredBindings);
    }

    public CompileTimePropertyResult Get(
        CompileTimeObjectValue receiver,
        string propertyName,
        CompileTimePropertyContext context)
    {
        object target = receiver is CompileTimeValue.Syntax syntax ? syntax.Value : receiver;
        var property = FindProperty(target.GetType(), propertyName);
        if (property is null)
        {
            return FindBinding(target.GetType())?.GetDynamicProperty(target, propertyName, context)
                ?? new CompileTimePropertyResult.Missing();
        }

        try
        {
            var invocationArguments = property.IsLegacy
                ? new object?[] { target, context }
                : [context, target];
            var result = property.Method.Invoke(property.Target, invocationArguments);
            if (result is CompileTimePropertyResult explicitResult)
            {
                return explicitResult;
            }

            if (result is null
                && typeof(CompileTimePropertyResult).IsAssignableFrom(property.Method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"Compile-time property handler '{property.Method.DeclaringType?.FullName}.{property.Method.Name}' returned null instead of a result.");
            }

            if (CompileTimeValueConverter.TryConvertReturnValue(result, out var value))
            {
                return CompileTimePropertyResult.From(value);
            }

            throw new InvalidOperationException(
                $"Compile-time property handler '{property.Method.DeclaringType?.FullName}.{property.Method.Name}' returned an unsupported value.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"Compile-time property handler '{property.Method.DeclaringType?.FullName}.{property.Method.Name}' failed.",
                exception.InnerException);
        }
    }

    private RegisteredProperty? FindProperty(Type receiverType, string propertyName)
    {
        for (var type = receiverType; type is not null; type = type.BaseType)
        {
            if (_properties.TryGetValue((type, propertyName), out var property))
            {
                return property;
            }
        }

        return null;
    }

    private CompileTimeTypeBinding? FindBinding(Type receiverType)
    {
        for (var type = receiverType; type is not null; type = type.BaseType)
        {
            if (_bindings.TryGetValue(type, out var binding))
            {
                return binding;
            }
        }

        return null;
    }

    private static bool ValidateHandler(
        CompileTimeTypeBinding binding,
        MethodInfo method,
        CompileTimePropertyAttribute marker)
    {
        if (method.IsStatic)
        {
            throw InvalidHandler(method, "must be an instance method returning CompileTimePropertyResult");
        }

        if (string.IsNullOrWhiteSpace(marker.PropertyName))
        {
            throw InvalidHandler(method, "must declare a non-empty property name");
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 2
            && parameters[0].ParameterType == binding.ReceiverType
            && parameters[1].ParameterType == typeof(CompileTimePropertyContext))
        {
            if (method.ReturnType != typeof(CompileTimePropertyResult))
            {
                throw InvalidHandler(
                    method,
                    "uses a legacy signature and must return CompileTimePropertyResult");
            }

            return true;
        }

        if (parameters.Length != 2
            || parameters[0].ParameterType != typeof(CompileTimePropertyContext)
            || parameters[1].ParameterType != binding.ReceiverType)
        {
            throw InvalidHandler(
                method,
                $"must accept either ({binding.ReceiverType.Name}, CompileTimePropertyContext) or (CompileTimePropertyContext, {binding.ReceiverType.Name})");
        }

        if (!CompileTimeValueConverter.IsSupportedReturnType(
                method.ReturnType,
                typeof(CompileTimePropertyResult)))
        {
            throw InvalidHandler(method, $"returns unsupported type '{method.ReturnType.Name}'");
        }

        return false;
    }

    private static InvalidOperationException InvalidHandler(MethodInfo method, string requirement) =>
        new($"Compile-time property handler '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");

    private sealed record RegisteredProperty(
        CompileTimeTypeBinding Target,
        MethodInfo Method,
        bool IsLegacy);
}
