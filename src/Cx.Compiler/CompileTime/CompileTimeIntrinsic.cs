using System.Reflection;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeIntrinsicContext(
    Location Location,
    IReadOnlyList<CompileTimeValue> Arguments,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics,
    Func<ExpressionNode, CompileTimeValue?> Evaluate);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class CompileTimeIntrinsicAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

internal abstract class CompileTimeIntrinsicBinding;

// Compatibility contract for externally registered intrinsics. Built-ins use typed bindings.
internal interface ICompileTimeIntrinsic
{
    string Name { get; }

    CompileTimeValue? Invoke(CompileTimeIntrinsicContext context);
}

internal sealed class CompileTimeIntrinsicRegistry
{
    private readonly Dictionary<string, ICompileTimeIntrinsic> _intrinsics = new(StringComparer.Ordinal);

    public bool Register(ICompileTimeIntrinsic intrinsic) =>
        _intrinsics.TryAdd(intrinsic.Name, intrinsic);

    internal void Register(CompileTimeIntrinsicBinding binding)
    {
        var methods = binding.GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var method in methods)
        {
            var marker = method.GetCustomAttribute<CompileTimeIntrinsicAttribute>();
            if (marker is null)
            {
                continue;
            }

            var registered = RegisteredIntrinsic.Create(binding, method, marker);
            if (!_intrinsics.TryGetValue(marker.Name, out var existing))
            {
                _intrinsics.Add(marker.Name, new TypedCompileTimeIntrinsic(marker.Name, registered));
                continue;
            }

            if (existing is not TypedCompileTimeIntrinsic typed)
            {
                throw new InvalidOperationException(
                    $"Compile-time intrinsic '{marker.Name}' is registered by both a legacy intrinsic and a typed binding.");
            }

            typed.Add(registered);
        }
    }

    public bool TryGet(string name, out ICompileTimeIntrinsic intrinsic) =>
        _intrinsics.TryGetValue(name, out intrinsic!);

    public static CompileTimeIntrinsicRegistry CreateDefault()
    {
        var registry = new CompileTimeIntrinsicRegistry();
        foreach (var binding in BuiltInCompileTimeIntrinsics.Bindings)
        {
            registry.Register(binding);
        }

        return registry;
    }

    private sealed class TypedCompileTimeIntrinsic : ICompileTimeIntrinsic
    {
        private readonly List<RegisteredIntrinsic> _overloads;

        public TypedCompileTimeIntrinsic(string name, RegisteredIntrinsic first)
        {
            Name = name;
            _overloads = [first];
        }

        public string Name { get; }

        public void Add(RegisteredIntrinsic intrinsic)
        {
            if (_overloads.Any(existing => existing.HasSameSignature(intrinsic)))
            {
                throw new InvalidOperationException(
                    $"Duplicate compile-time intrinsic overload '{intrinsic.FormatSignature()}' is registered.");
            }

            _overloads.Add(intrinsic);
        }

        public CompileTimeValue? Invoke(CompileTimeIntrinsicContext context)
        {
            var matches = _overloads
                .Select(overload => overload.TryBind(context))
                .Where(bound => bound is not null)
                .Cast<BoundIntrinsic>()
                .OrderBy(bound => bound.Score)
                .ToList();
            if (matches.Count == 0)
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time intrinsic '{Name}' has no overload matching ({string.Join(", ", context.Arguments.Select(CompileTimeValueFacts.Describe))}). " +
                    $"Available overloads: {string.Join(", ", _overloads.Select(overload => overload.FormatSignature()))}.");
                return null;
            }

            var best = matches.TakeWhile(match => match.Score == matches[0].Score).ToList();
            if (best.Count > 1)
            {
                context.Diagnostics.Report(
                    context.Location,
                    $"Compile-time intrinsic '{Name}' is ambiguous between {string.Join(", ", best.Select(match => match.Intrinsic.FormatSignature()))}.");
                return null;
            }

            return best[0].Invoke();
        }
    }

    private sealed record RegisteredIntrinsic(
        CompileTimeIntrinsicBinding Target,
        MethodInfo Method,
        IReadOnlyList<ParameterInfo> ScriptParameters,
        bool IsVariadic)
    {
        public static RegisteredIntrinsic Create(
            CompileTimeIntrinsicBinding target,
            MethodInfo method,
            CompileTimeIntrinsicAttribute marker)
        {
            if (method.IsStatic)
            {
                throw InvalidHandler(method, "must be an instance method");
            }

            if (string.IsNullOrWhiteSpace(marker.Name))
            {
                throw InvalidHandler(method, "must declare a non-empty intrinsic name");
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0
                || parameters[0].ParameterType != typeof(CompileTimeIntrinsicContext))
            {
                throw InvalidHandler(method, "must accept CompileTimeIntrinsicContext as its first parameter");
            }

            var scriptParameters = parameters.Skip(1).ToArray();
            if (scriptParameters.Any(parameter =>
                    parameter.IsOut
                    || parameter.ParameterType.IsByRef
                    || parameter.IsOptional))
            {
                throw InvalidHandler(method, "cannot use ref, out, or optional script parameters");
            }

            var variadicParameters = scriptParameters
                .Select((parameter, index) => (parameter, index))
                .Where(item => item.parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
                .ToList();
            if (variadicParameters.Count > 1
                || variadicParameters.Count == 1
                    && (variadicParameters[0].index != scriptParameters.Length - 1
                        || !variadicParameters[0].parameter.ParameterType.IsArray))
            {
                throw InvalidHandler(method, "may only use params on its final array parameter");
            }

            if (!CompileTimeValueConverter.IsSupportedReturnType(
                    method.ReturnType,
                    explicitResultType: null))
            {
                throw InvalidHandler(method, $"returns unsupported type '{method.ReturnType.Name}'");
            }

            return new RegisteredIntrinsic(
                target,
                method,
                scriptParameters,
                variadicParameters.Count == 1);
        }

        public BoundIntrinsic? TryBind(CompileTimeIntrinsicContext context)
        {
            var fixedCount = IsVariadic ? ScriptParameters.Count - 1 : ScriptParameters.Count;
            if ((!IsVariadic && context.Arguments.Count != fixedCount)
                || (IsVariadic && context.Arguments.Count < fixedCount))
            {
                return null;
            }

            var invocationArguments = new object?[ScriptParameters.Count + 1];
            invocationArguments[0] = context;
            var score = 0;
            for (var index = 0; index < fixedCount; index++)
            {
                if (!CompileTimeValueConverter.TryConvertArgument(
                        context.Arguments[index],
                        ScriptParameters[index].ParameterType,
                        out var converted,
                        out var conversionScore))
                {
                    return null;
                }

                invocationArguments[index + 1] = converted;
                score += conversionScore;
            }

            if (IsVariadic)
            {
                var elementType = ScriptParameters[^1].ParameterType.GetElementType()!;
                var values = Array.CreateInstance(elementType, context.Arguments.Count - fixedCount);
                for (var index = fixedCount; index < context.Arguments.Count; index++)
                {
                    if (!CompileTimeValueConverter.TryConvertArgument(
                            context.Arguments[index],
                            elementType,
                            out var converted,
                            out var conversionScore))
                    {
                        return null;
                    }

                    values.SetValue(converted, index - fixedCount);
                    score += conversionScore;
                }

                invocationArguments[^1] = values;
            }

            return new BoundIntrinsic(this, invocationArguments, score);
        }

        public bool HasSameSignature(RegisteredIntrinsic other) =>
            IsVariadic == other.IsVariadic
            && ScriptParameters
                .Select(parameter => parameter.ParameterType)
                .SequenceEqual(other.ScriptParameters.Select(parameter => parameter.ParameterType));

        public string FormatSignature() =>
            $"{Method.GetCustomAttribute<CompileTimeIntrinsicAttribute>()!.Name}" +
            $"({string.Join(", ", ScriptParameters.Select(parameter =>
                IsVariadic && parameter == ScriptParameters[^1]
                    ? $"params {parameter.ParameterType.GetElementType()!.Name}[]"
                    : parameter.ParameterType.Name))})";

        private static InvalidOperationException InvalidHandler(MethodInfo method, string requirement) =>
            new($"Compile-time intrinsic handler '{method.DeclaringType?.FullName}.{method.Name}' {requirement}.");
    }

    private sealed record BoundIntrinsic(
        RegisteredIntrinsic Intrinsic,
        object?[] Arguments,
        int Score)
    {
        public CompileTimeValue? Invoke()
        {
            try
            {
                var result = Intrinsic.Method.Invoke(Intrinsic.Target, Arguments);
                if (result is null)
                {
                    return null;
                }

                if (result is CompileTimeValue value)
                {
                    return value;
                }

                if (CompileTimeValueConverter.TryConvertReturnValue(result, out value))
                {
                    return value;
                }

                throw new InvalidOperationException(
                    $"Compile-time intrinsic handler '{Intrinsic.Method.DeclaringType?.FullName}.{Intrinsic.Method.Name}' returned an unsupported value.");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException(
                    $"Compile-time intrinsic handler '{Intrinsic.Method.DeclaringType?.FullName}.{Intrinsic.Method.Name}' failed.",
                    exception.InnerException);
            }
        }
    }
}
