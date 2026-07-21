using System.Reflection;

namespace Cx.Compiler.CompileTime;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class CompileTimeMethodAttribute(string methodName) : Attribute
{
    public string MethodName { get; } = methodName;
}

internal abstract class CompileTimeScriptObject
{
    public virtual string? GlobalName => null;

    public abstract Type ReceiverType { get; }

    public virtual CompileTimePropertyResult GetDynamicProperty(
        object receiver,
        string propertyName,
        CompileTimePropertyContext context) =>
        new CompileTimePropertyResult.Missing();
}

internal sealed class CompileTimeMethodRegistry
{
    private static readonly Lazy<CompileTimeMethodRegistry> DefaultRegistry = new(
        () => CreateFromObjects(CompileTimeBuiltIns.Objects));

    private readonly IReadOnlyDictionary<(string ObjectName, string MethodName), RegisteredMethod> _objectMethods;
    private readonly IReadOnlyDictionary<(Type ReceiverType, string MethodName), RegisteredMethod> _receiverMethods;

    private CompileTimeMethodRegistry(
        IReadOnlyDictionary<(string ObjectName, string MethodName), RegisteredMethod> objectMethods,
        IReadOnlyDictionary<(Type ReceiverType, string MethodName), RegisteredMethod> receiverMethods)
    {
        _objectMethods = objectMethods;
        _receiverMethods = receiverMethods;
    }

    public static CompileTimeMethodRegistry Default => DefaultRegistry.Value;

    internal static CompileTimeMethodRegistry CreateFromObjects(
        params CompileTimeScriptObject[] objects) =>
        CreateFromObjects((IEnumerable<CompileTimeScriptObject>)objects);

    internal static CompileTimeMethodRegistry CreateFromObjects(
        IEnumerable<CompileTimeScriptObject> objects)
    {
        var objectMethods = new Dictionary<(string, string), RegisteredMethod>();
        var receiverMethods = new Dictionary<(Type, string), RegisteredMethod>();
        foreach (var scriptObject in objects)
        {
            var methods = scriptObject.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var marker = method.GetCustomAttribute<CompileTimeMethodAttribute>();
                if (marker is null)
                {
                    continue;
                }

                var kind = ValidateHandler(scriptObject, method, marker);
                var registered = new RegisteredMethod(scriptObject, method, kind);
                if (kind == HandlerKind.Object)
                {
                    AddUnique(
                        objectMethods,
                        (scriptObject.GlobalName!, marker.MethodName),
                        registered,
                        $"compile-time object method '{scriptObject.GlobalName}.{marker.MethodName}'");
                }
                else
                {
                    AddUnique(
                        receiverMethods,
                        (scriptObject.ReceiverType, marker.MethodName),
                        registered,
                        $"compile-time receiver method '{scriptObject.ReceiverType.Name}.{marker.MethodName}'");
                }
            }
        }

        return new CompileTimeMethodRegistry(objectMethods, receiverMethods);
    }

    public CompileTimeMethodResult Invoke(
        CompileTimeObjectValue receiver,
        string methodName,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        RegisteredMethod? registered;
        object? syntaxReceiver = null;
        if (receiver is CompileTimeScriptObjectValue objectValue)
        {
            _objectMethods.TryGetValue((objectValue.Definition.GlobalName!, methodName), out registered);
        }
        else
        {
            syntaxReceiver = receiver is CompileTimeValue.Syntax syntax ? syntax.Value : receiver;
            registered = FindReceiverMethod(syntaxReceiver.GetType(), methodName);
        }

        if (registered is null)
        {
            return new CompileTimeMethodResult.Missing();
        }

        try
        {
            var invocationArguments = registered.Kind == HandlerKind.Object
                ? new object?[] { arguments, context }
                : [syntaxReceiver, arguments, context];
            return (CompileTimeMethodResult)registered.Method.Invoke(
                registered.Target,
                invocationArguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"Compile-time method handler '{registered.Method.DeclaringType?.FullName}.{registered.Method.Name}' failed.",
                exception.InnerException);
        }
    }

    private RegisteredMethod? FindReceiverMethod(Type receiverType, string methodName)
    {
        for (var type = receiverType; type is not null; type = type.BaseType)
        {
            if (_receiverMethods.TryGetValue((type, methodName), out var method))
            {
                return method;
            }
        }

        return null;
    }

    private static HandlerKind ValidateHandler(
        CompileTimeScriptObject scriptObject,
        MethodInfo method,
        CompileTimeMethodAttribute marker)
    {
        if (method.IsStatic || method.ReturnType != typeof(CompileTimeMethodResult))
        {
            throw InvalidHandler(method, "must be an instance method returning CompileTimeMethodResult");
        }

        if (string.IsNullOrWhiteSpace(marker.MethodName))
        {
            throw InvalidHandler(method, "must declare a non-empty method name");
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 2
            && parameters[0].ParameterType == typeof(IReadOnlyList<CompileTimeValue>)
            && parameters[1].ParameterType == typeof(CompileTimeMethodContext))
        {
            if (scriptObject.GlobalName is null)
            {
                throw InvalidHandler(method, "declares an object method, but its script object has no global name");
            }

            return HandlerKind.Object;
        }

        if (parameters.Length == 3
            && parameters[0].ParameterType == scriptObject.ReceiverType
            && parameters[1].ParameterType == typeof(IReadOnlyList<CompileTimeValue>)
            && parameters[2].ParameterType == typeof(CompileTimeMethodContext))
        {
            return HandlerKind.Receiver;
        }

        throw InvalidHandler(
            method,
            $"must accept either (IReadOnlyList<CompileTimeValue>, CompileTimeMethodContext) or ({scriptObject.ReceiverType.Name}, IReadOnlyList<CompileTimeValue>, CompileTimeMethodContext)");
    }

    private static InvalidOperationException InvalidHandler(MethodInfo method, string requirement) =>
        new($"Compile-time method handler '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");

    private static void AddUnique<TKey>(
        IDictionary<TKey, RegisteredMethod> methods,
        TKey key,
        RegisteredMethod method,
        string description)
        where TKey : notnull
    {
        if (methods.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException(
                $"Duplicate {description} is registered by '{existing.Method.DeclaringType?.FullName}.{existing.Method.Name}' and '{method.Method.DeclaringType?.FullName}.{method.Method.Name}'.");
        }

        methods.Add(key, method);
    }

    private enum HandlerKind
    {
        Object,
        Receiver,
    }

    private sealed record RegisteredMethod(
        CompileTimeScriptObject Target,
        MethodInfo Method,
        HandlerKind Kind);
}
