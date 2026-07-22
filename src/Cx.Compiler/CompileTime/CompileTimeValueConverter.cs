using System.Collections;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;

namespace Cx.Compiler.CompileTime;

internal static class CompileTimeValueConverter
{
    public static bool TryConvertReturnValue(object? result, out CompileTimeValue value)
    {
        switch (result)
        {
            case null:
                value = new CompileTimeValue.Null();
                return true;
            case CompileTimeValue compileTimeValue:
                value = compileTimeValue;
                return true;
            case string text:
                value = new CompileTimeValue.String(text);
                return true;
            case bool boolean:
                value = new CompileTimeValue.Boolean(boolean);
                return true;
            case sbyte or byte or short or ushort or int or uint or long:
                value = new CompileTimeValue.Integer(Convert.ToInt64(result));
                return true;
            case TypeRef type:
                value = new CompileTimeValue.Type(type);
                return true;
            case SyntaxNode syntax:
                value = new CompileTimeValue.Syntax(syntax);
                return true;
            case IEnumerable sequence:
                var values = new List<CompileTimeValue>();
                foreach (var element in sequence)
                {
                    if (!TryConvertReturnValue(element, out var convertedElement))
                    {
                        value = null!;
                        return false;
                    }

                    values.Add(convertedElement);
                }

                value = new CompileTimeValue.List(values);
                return true;
            default:
                value = null!;
                return false;
        }
    }

    public static bool IsSupportedReturnType(Type type, Type explicitResultType)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            return IsSupportedReturnType(nullableType, explicitResultType);
        }

        if (explicitResultType.IsAssignableFrom(type)
            || typeof(CompileTimeValue).IsAssignableFrom(type)
            || type == typeof(string)
            || type == typeof(bool)
            || type == typeof(sbyte)
            || type == typeof(byte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || typeof(TypeRef).IsAssignableFrom(type)
            || typeof(SyntaxNode).IsAssignableFrom(type))
        {
            return true;
        }

        var elementType = GetEnumerableElementType(type);
        return elementType is not null
            && !explicitResultType.IsAssignableFrom(elementType)
            && IsSupportedReturnType(elementType, explicitResultType);
    }

    public static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type
            .GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))?
            .GetGenericArguments()[0];
    }
}
