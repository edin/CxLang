using System.Collections;
using System.Reflection;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.CompileTime;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class CompileTimeMethodAttribute(string methodName) : Attribute
{
    public string MethodName { get; } = methodName;
}

internal sealed class CompileTimeMethodRegistry
{
    private static readonly Lazy<CompileTimeMethodRegistry> DefaultRegistry = new(
        () => CreateFromBindings(BuiltInCompileTimeBindings.Bindings));

    private readonly IReadOnlyDictionary<(string ObjectName, string MethodName), IReadOnlyList<RegisteredMethod>> _objectMethods;
    private readonly IReadOnlyDictionary<(Type ReceiverType, string MethodName), IReadOnlyList<RegisteredMethod>> _receiverMethods;

    private CompileTimeMethodRegistry(
        IReadOnlyDictionary<(string ObjectName, string MethodName), IReadOnlyList<RegisteredMethod>> objectMethods,
        IReadOnlyDictionary<(Type ReceiverType, string MethodName), IReadOnlyList<RegisteredMethod>> receiverMethods)
    {
        _objectMethods = objectMethods;
        _receiverMethods = receiverMethods;
    }

    public static CompileTimeMethodRegistry Default => DefaultRegistry.Value;

    internal static CompileTimeMethodRegistry CreateFromBindings(
        params CompileTimeTypeBinding[] bindings) =>
        CreateFromBindings((IEnumerable<CompileTimeTypeBinding>)bindings);

    internal static CompileTimeMethodRegistry CreateFromBindings(
        IEnumerable<CompileTimeTypeBinding> bindings)
    {
        var objectMethods = new Dictionary<(string, string), List<RegisteredMethod>>();
        var receiverMethods = new Dictionary<(Type, string), List<RegisteredMethod>>();
        foreach (var binding in bindings)
        {
            var methods = binding.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                var marker = method.GetCustomAttribute<CompileTimeMethodAttribute>();
                if (marker is null)
                {
                    continue;
                }

                var registered = ValidateHandler(binding, method, marker);
                if (registered.Kind == HandlerKind.Object)
                {
                    AddOverload(
                        objectMethods,
                        (binding.GlobalName!, marker.MethodName),
                        registered,
                        $"compile-time object method '{binding.GlobalName}.{marker.MethodName}'");
                }
                else
                {
                    AddOverload(
                        receiverMethods,
                        (binding.ReceiverType, marker.MethodName),
                        registered,
                        $"compile-time receiver method '{binding.ReceiverType.Name}.{marker.MethodName}'");
                }
            }
        }

        return new CompileTimeMethodRegistry(
            Freeze(objectMethods),
            Freeze(receiverMethods));
    }

    public CompileTimeMethodResult Invoke(
        CompileTimeObjectValue receiver,
        string methodName,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        IReadOnlyList<RegisteredMethod>? candidates;
        object? syntaxReceiver = null;
        if (receiver is CompileTimeGlobalObjectValue objectValue)
        {
            _objectMethods.TryGetValue((objectValue.Binding.GlobalName!, methodName), out candidates);
        }
        else
        {
            syntaxReceiver = receiver is CompileTimeValue.Syntax syntax ? syntax.Value : receiver;
            candidates = FindReceiverMethods(syntaxReceiver.GetType(), methodName);
        }

        if (candidates is null)
        {
            return new CompileTimeMethodResult.Missing();
        }

        var matches = candidates
            .Where(candidate => !candidate.IsLegacy)
            .Select(candidate => TryBind(candidate, syntaxReceiver, arguments, context))
            .Where(match => match is not null)
            .Cast<BoundMethod>()
            .OrderBy(match => match.Score)
            .ToList();

        if (matches.Count > 0)
        {
            var bestMatches = matches.TakeWhile(match => match.Score == matches[0].Score).ToList();
            if (bestMatches.Count > 1)
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time method call is ambiguous between {string.Join(", ", bestMatches.Select(match => FormatSignature(match.Method)))}.");
                return new CompileTimeMethodResult.Failed();
            }

            return InvokeBound(bestMatches[0]);
        }

        var legacy = candidates.SingleOrDefault(candidate => candidate.IsLegacy);
        if (legacy is not null)
        {
            return InvokeLegacy(legacy, syntaxReceiver, arguments, context);
        }

        ReportNoMatchingOverload(candidates, arguments, context);
        return new CompileTimeMethodResult.Failed();
    }

    private static void ReportNoMatchingOverload(
        IReadOnlyList<RegisteredMethod> candidates,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        var arityMatches = candidates
            .Where(candidate => !candidate.IsLegacy && candidate.ScriptParameters.Count == arguments.Count)
            .Select(candidate =>
            {
                var failedIndex = 0;
                while (failedIndex < arguments.Count
                    && TryConvert(
                        arguments[failedIndex],
                        candidate.ScriptParameters[failedIndex].ParameterType,
                        out _,
                        out _))
                {
                    failedIndex++;
                }

                return (Candidate: candidate, FailedIndex: failedIndex);
            })
            .OrderByDescending(match => match.FailedIndex)
            .ToList();

        if (arityMatches.Count > 0
            && arityMatches[0] is { } closest
            && closest.FailedIndex < arguments.Count)
        {
            var expectedType = closest.Candidate.ScriptParameters[closest.FailedIndex].ParameterType;
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time method '{FormatQualifiedName(closest.Candidate)}' expects {DescribeExpectedArgument(expectedType)} as argument {closest.FailedIndex + 1}, " +
                $"but received {CompileTimeValueFacts.Describe(arguments[closest.FailedIndex])}.");
            return;
        }

        var method = candidates[0];
        var expectedArities = candidates
            .Where(candidate => !candidate.IsLegacy)
            .Select(candidate => candidate.ScriptParameters.Count)
            .Distinct()
            .Order()
            .ToList();
        context.Diagnostics.Report(
            context.Location,
            $"Compile-time method '{FormatQualifiedName(method)}' expects {FormatExpectedArities(expectedArities)}, but received {arguments.Count}. " +
            $"Available overloads: {string.Join(", ", candidates.Select(FormatSignature))}.");
    }

    private static string DescribeExpectedArgument(Type type)
    {
        if (type == typeof(string))
        {
            return "a string or name";
        }

        if (typeof(TypeRef).IsAssignableFrom(type))
        {
            return "a type";
        }

        if (typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return $"{DescribeSyntaxType(type)} syntax";
        }

        if (CompileTimeValueConverter.GetEnumerableElementType(type) is { } elementType)
        {
            if (typeof(TypeRef).IsAssignableFrom(elementType))
            {
                return "type items";
            }

            if (typeof(SyntaxNode).IsAssignableFrom(elementType))
            {
                return $"{DescribeSyntaxType(elementType)} syntax items";
            }

            return $"{elementType.Name} items";
        }

        return type == typeof(bool)
            ? "a boolean"
            : type == typeof(long) || type == typeof(int)
                ? "an integer"
                : $"a {type.Name}";
    }

    private static string DescribeSyntaxType(Type type)
    {
        var name = type.Name;
        if (name.EndsWith("ApplicationNode", StringComparison.Ordinal))
        {
            name = name[..^"ApplicationNode".Length];
        }
        else if (name.EndsWith("Node", StringComparison.Ordinal))
        {
            name = name[..^"Node".Length];
        }

        return string.Concat(name.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
    }

    private static string FormatExpectedArities(IReadOnlyList<int> arities) => arities.Count switch
    {
        0 => "a supported argument list",
        1 => $"{arities[0]} argument(s)",
        2 => $"{arities[0]} or {arities[1]} arguments",
        _ => $"one of {string.Join(", ", arities)} arguments",
    };

    private static string FormatQualifiedName(RegisteredMethod method)
    {
        var marker = method.Method.GetCustomAttribute<CompileTimeMethodAttribute>()!;
        var owner = method.Kind == HandlerKind.Object
            ? method.Target.GlobalName!
            : method.Target.GlobalName?.ToLowerInvariant()
                ?? method.Target.ReceiverType.Name
                    .Replace("CompileTimeValue", string.Empty, StringComparison.Ordinal)
                    .Replace("Node", string.Empty, StringComparison.Ordinal)
                    .ToLowerInvariant();
        return $"{owner}.{marker.MethodName}";
    }

    private static CompileTimeMethodResult InvokeBound(BoundMethod bound) =>
        InvokeReflected(bound.Method, bound.InvocationArguments);

    private static CompileTimeMethodResult InvokeLegacy(
        RegisteredMethod registered,
        object? receiver,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        object?[] invocationArguments = registered.Kind == HandlerKind.Object
            ? [arguments, context]
            : [receiver, arguments, context];
        return InvokeReflected(registered, invocationArguments);
    }

    private static CompileTimeMethodResult InvokeReflected(
        RegisteredMethod registered,
        object?[] invocationArguments)
    {
        try
        {
            var result = registered.Method.Invoke(registered.Target, invocationArguments);
            if (result is CompileTimeMethodResult explicitResult)
            {
                return explicitResult;
            }

            if (result is null
                && typeof(CompileTimeMethodResult).IsAssignableFrom(registered.Method.ReturnType))
            {
                throw new InvalidOperationException(
                    $"Compile-time method handler '{registered.Method.DeclaringType?.FullName}.{registered.Method.Name}' returned null instead of a result.");
            }

            if (CompileTimeValueConverter.TryConvertReturnValue(result, out var value))
            {
                return CompileTimeMethodResult.From(value);
            }

            throw new InvalidOperationException(
                $"Compile-time method handler '{registered.Method.DeclaringType?.FullName}.{registered.Method.Name}' returned an unsupported value.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException(
                $"Compile-time method handler '{registered.Method.DeclaringType?.FullName}.{registered.Method.Name}' failed.",
                exception.InnerException);
        }
    }

    private IReadOnlyList<RegisteredMethod>? FindReceiverMethods(Type receiverType, string methodName)
    {
        for (var type = receiverType; type is not null; type = type.BaseType)
        {
            if (_receiverMethods.TryGetValue((type, methodName), out var methods))
            {
                return methods;
            }
        }

        return null;
    }

    private static BoundMethod? TryBind(
        RegisteredMethod registered,
        object? receiver,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (registered.ScriptParameters.Count != arguments.Count)
        {
            return null;
        }

        var invocationArguments = new object?[registered.Method.GetParameters().Length];
        invocationArguments[0] = context;
        var offset = 1;
        if (registered.Kind == HandlerKind.Receiver)
        {
            invocationArguments[offset++] = receiver;
        }

        var score = 0;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!TryConvert(arguments[index], registered.ScriptParameters[index].ParameterType, out var converted, out var conversionScore))
            {
                return null;
            }

            invocationArguments[offset + index] = converted;
            score += conversionScore;
        }

        return new BoundMethod(registered, invocationArguments, score);
    }

    private static bool TryConvert(
        CompileTimeValue value,
        Type targetType,
        out object? converted,
        out int score)
    {
        if (targetType == value.GetType())
        {
            converted = value;
            score = 0;
            return true;
        }

        if (typeof(CompileTimeValue).IsAssignableFrom(targetType) && targetType.IsInstanceOfType(value))
        {
            converted = value;
            score = targetType == typeof(CompileTimeValue) ? 20 : 2;
            return true;
        }

        if (value is CompileTimeValue.Syntax syntax && targetType.IsInstanceOfType(syntax.Value))
        {
            converted = syntax.Value;
            score = targetType == syntax.Value.GetType() ? 1 : 3;
            return true;
        }

        if (value is CompileTimeValue.Type type && targetType.IsInstanceOfType(type.Value))
        {
            converted = type.Value;
            score = targetType == type.Value.GetType() ? 1 : 2;
            return true;
        }

        if (value is CompileTimeValue.String text && targetType == typeof(string))
        {
            converted = text.Value;
            score = 1;
            return true;
        }

        if (value is CompileTimeValue.Name name && targetType == typeof(string))
        {
            converted = name.Value;
            score = 2;
            return true;
        }

        if (value is CompileTimeValue.Boolean boolean && targetType == typeof(bool))
        {
            converted = boolean.Value;
            score = 1;
            return true;
        }

        if (value is CompileTimeValue.Integer integer)
        {
            if (targetType == typeof(long))
            {
                converted = integer.Value;
                score = 1;
                return true;
            }

            if (targetType == typeof(int) && integer.Value is >= int.MinValue and <= int.MaxValue)
            {
                converted = (int)integer.Value;
                score = 2;
                return true;
            }
        }

        if (value is CompileTimeValue.List list
            && TryConvertList(list, targetType, out converted, out score))
        {
            return true;
        }

        if (value is CompileTimeValue.Null
            && (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null))
        {
            converted = null;
            score = 10;
            return true;
        }

        converted = null;
        score = 0;
        return false;
    }

    private static bool TryConvertList(
        CompileTimeValue.List list,
        Type targetType,
        out object? converted,
        out int score)
    {
        var elementType = CompileTimeValueConverter.GetEnumerableElementType(targetType);
        if (elementType is null)
        {
            converted = null;
            score = 0;
            return false;
        }

        var convertedItems = (IList)Activator.CreateInstance(
            typeof(List<>).MakeGenericType(elementType))!;
        score = 2;
        foreach (var item in list.Values)
        {
            if (!TryConvert(item, elementType, out var convertedItem, out var itemScore))
            {
                converted = null;
                score = 0;
                return false;
            }

            convertedItems.Add(convertedItem);
            score += itemScore;
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, convertedItems.Count);
            convertedItems.CopyTo(array, 0);
            converted = array;
            return true;
        }

        if (!targetType.IsInstanceOfType(convertedItems))
        {
            converted = null;
            score = 0;
            return false;
        }

        converted = convertedItems;
        return true;
    }

    private static RegisteredMethod ValidateHandler(
        CompileTimeTypeBinding binding,
        MethodInfo method,
        CompileTimeMethodAttribute marker)
    {
        if (method.IsStatic)
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
            ValidateLegacyReturnType(method);
            if (binding.GlobalName is null)
            {
                throw InvalidHandler(method, "declares an object method, but its script object has no global name");
            }

            return new RegisteredMethod(binding, method, HandlerKind.Object, true, []);
        }

        if (parameters.Length == 3
            && parameters[0].ParameterType == binding.ReceiverType
            && parameters[1].ParameterType == typeof(IReadOnlyList<CompileTimeValue>)
            && parameters[2].ParameterType == typeof(CompileTimeMethodContext))
        {
            ValidateLegacyReturnType(method);
            return new RegisteredMethod(binding, method, HandlerKind.Receiver, true, []);
        }

        if (parameters.Length == 0 || parameters[0].ParameterType != typeof(CompileTimeMethodContext))
        {
            throw InvalidHandler(
                method,
                $"must use a legacy handler signature or accept CompileTimeMethodContext as its first parameter");
        }

        var kind = parameters.Length >= 2 && parameters[1].ParameterType == binding.ReceiverType
            ? HandlerKind.Receiver
            : HandlerKind.Object;
        if (kind == HandlerKind.Object && binding.GlobalName is null)
        {
            throw InvalidHandler(method, "declares an object method, but its script object has no global name");
        }

        if (!CompileTimeValueConverter.IsSupportedReturnType(
                method.ReturnType,
                typeof(CompileTimeMethodResult)))
        {
            throw InvalidHandler(
                method,
                $"returns unsupported type '{method.ReturnType.Name}'");
        }

        var scriptParameterOffset = kind == HandlerKind.Receiver ? 2 : 1;
        var scriptParameters = parameters.Skip(scriptParameterOffset).ToArray();
        if (scriptParameters.Any(parameter => parameter.IsOut || parameter.ParameterType.IsByRef || parameter.IsOptional))
        {
            throw InvalidHandler(method, "cannot currently use ref, out, or optional script parameters");
        }

        return new RegisteredMethod(binding, method, kind, false, scriptParameters);
    }

    private static void ValidateLegacyReturnType(MethodInfo method)
    {
        if (method.ReturnType != typeof(CompileTimeMethodResult))
        {
            throw InvalidHandler(method, "uses a legacy signature and must return CompileTimeMethodResult");
        }
    }

    private static InvalidOperationException InvalidHandler(MethodInfo method, string requirement) =>
        new($"Compile-time method handler '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");

    private static void AddOverload<TKey>(
        IDictionary<TKey, List<RegisteredMethod>> methods,
        TKey key,
        RegisteredMethod method,
        string description)
        where TKey : notnull
    {
        if (!methods.TryGetValue(key, out var overloads))
        {
            overloads = [];
            methods.Add(key, overloads);
        }

        var duplicate = overloads.FirstOrDefault(existing => HaveSameSignature(existing, method));
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate {description} is registered by '{duplicate.Method.DeclaringType?.FullName}.{duplicate.Method.Name}' and '{method.Method.DeclaringType?.FullName}.{method.Method.Name}'.");
        }

        overloads.Add(method);
    }

    private static bool HaveSameSignature(RegisteredMethod left, RegisteredMethod right) =>
        left.IsLegacy == right.IsLegacy
        && (left.IsLegacy || left.ScriptParameters
            .Select(parameter => parameter.ParameterType)
            .SequenceEqual(right.ScriptParameters.Select(parameter => parameter.ParameterType)));

    private static IReadOnlyDictionary<TKey, IReadOnlyList<RegisteredMethod>> Freeze<TKey>(
        Dictionary<TKey, List<RegisteredMethod>> methods)
        where TKey : notnull =>
        methods.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RegisteredMethod>)pair.Value.ToArray());

    private static string FormatSignature(RegisteredMethod method) =>
        $"{method.Method.GetCustomAttribute<CompileTimeMethodAttribute>()!.MethodName}" +
        $"({string.Join(", ", method.ScriptParameters.Select(parameter => parameter.ParameterType.Name))})";

    private enum HandlerKind
    {
        Object,
        Receiver,
    }

    private sealed record RegisteredMethod(
        CompileTimeTypeBinding Target,
        MethodInfo Method,
        HandlerKind Kind,
        bool IsLegacy,
        IReadOnlyList<ParameterInfo> ScriptParameters);

    private sealed record BoundMethod(
        RegisteredMethod Method,
        object?[] InvocationArguments,
        int Score);
}
