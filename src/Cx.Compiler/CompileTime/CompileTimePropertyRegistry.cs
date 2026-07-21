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
        () => CreateFromObjects(CompileTimeBuiltIns.Objects));

    private readonly IReadOnlyDictionary<(Type ReceiverType, string PropertyName), RegisteredProperty> _properties;
    private readonly IReadOnlyDictionary<Type, CompileTimeScriptObject> _objects;

    private CompileTimePropertyRegistry(
        IReadOnlyDictionary<(Type ReceiverType, string PropertyName), RegisteredProperty> properties,
        IReadOnlyDictionary<Type, CompileTimeScriptObject> objects)
    {
        _properties = properties;
        _objects = objects;
    }

    public static CompileTimePropertyRegistry Default => DefaultRegistry.Value;

    internal static CompileTimePropertyRegistry CreateFromObjects(
        params CompileTimeScriptObject[] objects) =>
        CreateFromObjects((IEnumerable<CompileTimeScriptObject>)objects);

    internal static CompileTimePropertyRegistry CreateFromObjects(
        IEnumerable<CompileTimeScriptObject> objects)
    {
        var properties = new Dictionary<(Type, string), RegisteredProperty>();
        var registeredObjects = new Dictionary<Type, CompileTimeScriptObject>();
        foreach (var scriptObject in objects)
        {
            if (!registeredObjects.TryAdd(scriptObject.ReceiverType, scriptObject))
            {
                throw new InvalidOperationException(
                    $"Duplicate compile-time script object for receiver type '{scriptObject.ReceiverType.Name}'.");
            }

            var methods = scriptObject.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var marker = method.GetCustomAttribute<CompileTimePropertyAttribute>();
                if (marker is null)
                {
                    continue;
                }

                ValidateHandler(scriptObject, method, marker);
                var key = (scriptObject.ReceiverType, marker.PropertyName);
                if (properties.TryGetValue(key, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate compile-time property '{scriptObject.ReceiverType.Name}.{marker.PropertyName}' is registered by '{existing.Method.DeclaringType?.FullName}.{existing.Method.Name}' and '{method.DeclaringType?.FullName}.{method.Name}'.");
                }

                properties.Add(key, new RegisteredProperty(scriptObject, method));
            }
        }

        return new CompileTimePropertyRegistry(properties, registeredObjects);
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
            return FindObject(target.GetType())?.GetDynamicProperty(target, propertyName, context)
                ?? new CompileTimePropertyResult.Missing();
        }

        try
        {
            return (CompileTimePropertyResult)property.Method.Invoke(
                property.Target,
                [target, context])!;
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

    private CompileTimeScriptObject? FindObject(Type receiverType)
    {
        for (var type = receiverType; type is not null; type = type.BaseType)
        {
            if (_objects.TryGetValue(type, out var scriptObject))
            {
                return scriptObject;
            }
        }

        return null;
    }

    private static void ValidateHandler(
        CompileTimeScriptObject scriptObject,
        MethodInfo method,
        CompileTimePropertyAttribute marker)
    {
        if (method.IsStatic || method.ReturnType != typeof(CompileTimePropertyResult))
        {
            throw InvalidHandler(method, "must be an instance method returning CompileTimePropertyResult");
        }

        if (string.IsNullOrWhiteSpace(marker.PropertyName))
        {
            throw InvalidHandler(method, "must declare a non-empty property name");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 2
            || parameters[0].ParameterType != scriptObject.ReceiverType
            || parameters[1].ParameterType != typeof(CompileTimePropertyContext))
        {
            throw InvalidHandler(
                method,
                $"must accept ({scriptObject.ReceiverType.Name}, CompileTimePropertyContext)");
        }
    }

    private static InvalidOperationException InvalidHandler(MethodInfo method, string requirement) =>
        new($"Compile-time property handler '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");

    private sealed record RegisteredProperty(
        CompileTimeScriptObject Target,
        MethodInfo Method);
}
