using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal static class CompileTimeFunctionSignatureFacts
{
    public static TypeRef.Function? Create(
        FunctionNode function,
        ICompileTimeReflection reflection,
        DiagnosticBag diagnostics,
        Location location)
    {
        var parameters = function.Parameters.AsEnumerable();
        if (!function.IsStatic
            && function.OwnerTypeNode is not null
            && function.Parameters.FirstOrDefault()?.Name == "self")
        {
            parameters = parameters.Skip(1);
        }

        return Create(
            parameters.ToList(),
            function.ReturnTypeNode,
            reflection,
            diagnostics,
            location);
    }

    public static TypeRef.Function? Create(
        ExternFunctionNode function,
        ICompileTimeReflection reflection,
        DiagnosticBag diagnostics,
        Location location) =>
        Create(
            function.Parameters,
            function.ReturnTypeNode,
            reflection,
            diagnostics,
            location);

    public static TypeRef.Function Create(CompileTimeValue.ResolvedMethod method)
    {
        var parameters = method.Value.Parameters.AsEnumerable();
        if (!method.Value.Declaration.IsStatic
            && method.Value.Parameters.FirstOrDefault()?.Name == "self")
        {
            parameters = parameters.Skip(1);
        }

        return new TypeRef.Function(
            parameters.Select(parameter => parameter.Type).ToList(),
            method.Value.ReturnType,
            method.Value.Declaration.Parameters.Any(parameter => parameter.IsVariadic));
    }

    private static TypeRef.Function? Create(
        IReadOnlyList<ParameterNode> parameters,
        TypeNode? returnTypeNode,
        ICompileTimeReflection reflection,
        DiagnosticBag diagnostics,
        Location location)
    {
        var parameterTypes = new List<TypeRef>();
        foreach (var parameter in parameters.Where(parameter => !parameter.IsVariadic))
        {
            if (!reflection.TryGetType(parameter, out var parameterType))
            {
                diagnostics.Report(
                    location,
                    $"Could not resolve parameter type for compile-time function signature '{parameter.Name}'.");
                return null;
            }

            parameterTypes.Add(parameterType);
        }

        TypeRef returnType;
        if (returnTypeNode is null)
        {
            returnType = TypeRef.Void;
        }
        else if (!reflection.TryGetType(returnTypeNode, out returnType!))
        {
            diagnostics.Report(location, "Could not resolve return type for compile-time function signature.");
            return null;
        }

        return new TypeRef.Function(
            parameterTypes,
            returnType,
            parameters.Any(parameter => parameter.IsVariadic));
    }

    public static CompileTimeMethodResult Match(
        TypeRef.Function signature,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context)
    {
        if (arguments is not [CompileTimeValue.Type { Value: TypeRef.Function expected }])
        {
            context.Diagnostics.Report(
                context.Location,
                "Compile-time method 'match' expects exactly one function type.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Boolean(
            TypeIdentity.ResolvedEquals(signature, expected)));
    }
}
