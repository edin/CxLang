using System.Numerics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal enum PrimitiveTypeCategory
{
    Void,
    Boolean,
    Character,
    SignedInteger,
    UnsignedInteger,
    FloatingPoint,
}

internal sealed record PrimitiveTypeDescriptor(
    string Name,
    PrimitiveTypeCategory Category,
    int Rank = 0,
    int? BitWidth = null)
{
    public bool IsInteger =>
        Category is PrimitiveTypeCategory.SignedInteger
            or PrimitiveTypeCategory.UnsignedInteger;

    public bool IsNumeric =>
        IsInteger
        || Category is PrimitiveTypeCategory.Character
            or PrimitiveTypeCategory.FloatingPoint;

    public bool IsSigned => Category == PrimitiveTypeCategory.SignedInteger;
}

internal static class PrimitiveTypeRegistry
{
    private static readonly IReadOnlyDictionary<string, PrimitiveTypeDescriptor> Types =
        new[]
        {
            new PrimitiveTypeDescriptor("void", PrimitiveTypeCategory.Void),
            new PrimitiveTypeDescriptor("bool", PrimitiveTypeCategory.Boolean),
            new PrimitiveTypeDescriptor("char", PrimitiveTypeCategory.Character, BitWidth: 8),

            Signed("signed char", rank: 1, bits: 8),
            Unsigned("unsigned char", rank: 1, bits: 8),
            Signed("short", rank: 2, bits: 16),
            Unsigned("unsigned short", rank: 2, bits: 16),
            Signed("int", rank: 3, bits: 32),
            Unsigned("unsigned int", rank: 3, bits: 32),
            Signed("long", rank: 4),
            Unsigned("unsigned long", rank: 4),
            Signed("long long", rank: 5, bits: 64),
            Unsigned("unsigned long long", rank: 5, bits: 64),

            Signed("i8", rank: 1, bits: 8),
            Signed("i16", rank: 2, bits: 16),
            Signed("i32", rank: 3, bits: 32),
            Signed("i64", rank: 5, bits: 64),
            Unsigned("u8", rank: 1, bits: 8),
            Unsigned("u16", rank: 2, bits: 16),
            Unsigned("u32", rank: 3, bits: 32),
            Unsigned("u64", rank: 5, bits: 64),

            Unsigned("usize", rank: 4),

            Floating("float", rank: 1, bits: 32),
            Floating("double", rank: 2, bits: 64),
            Floating("long double", rank: 3),
        }
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    public static bool TryGet(TypeRef? type, out PrimitiveTypeDescriptor descriptor)
    {
        while (type is TypeRef.Const constType)
        {
            type = constType.Element;
        }

        if (type is TypeRef.Alias alias
            && Types.TryGetValue(alias.Name, out descriptor!))
        {
            return true;
        }

        type = type is null ? null : TypeRefFacts.UnwrapAlias(type);
        if (type is TypeRef.Named
            {
                Arguments.Count: 0,
                ModuleName: null,
            } named
            && Types.TryGetValue(named.Name, out descriptor!))
        {
            return true;
        }

        descriptor = null!;
        return false;
    }

    public static bool IsPrimitive(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Types.ContainsKey(name.Trim());

    public static bool IsNumeric(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && Types.TryGetValue(name.Trim(), out var descriptor)
        && descriptor.IsNumeric;

    private static PrimitiveTypeDescriptor Signed(string name, int rank, int? bits = null) =>
        new(name, PrimitiveTypeCategory.SignedInteger, rank, bits);

    private static PrimitiveTypeDescriptor Unsigned(string name, int rank, int? bits = null) =>
        new(name, PrimitiveTypeCategory.UnsignedInteger, rank, bits);

    private static PrimitiveTypeDescriptor Floating(string name, int rank, int? bits = null) =>
        new(name, PrimitiveTypeCategory.FloatingPoint, rank, bits);
}

internal sealed record PrimitiveOperatorResult(
    TypeRef? ResultType = null,
    string? Failure = null)
{
    public bool IsSupported => ResultType is not null;
}

internal readonly record struct PrimitiveOperand(
    TypeRef Type,
    BigInteger? IntegerLiteral = null)
{
    public static PrimitiveOperand FromExpression(TypeRef type, ExpressionNode expression) =>
        new(type, TryParseIntegerLiteral(expression));

    private static BigInteger? TryParseIntegerLiteral(ExpressionNode expression)
    {
        if (expression is not LiteralExpressionNode
            {
                Kind: LiteralKind.Integer,
            } literal)
        {
            return null;
        }

        return IntegerLiteralParser.TryParse(literal.LiteralText, out var value)
            ? value
            : null;
    }
}

internal static class PrimitiveSemantics
{
    public static PrimitiveOperatorResult ResolveBinary(
        BinaryOperator binaryOperator,
        TypeRef leftType,
        TypeRef rightType) =>
        ResolveBinary(
            binaryOperator,
            new PrimitiveOperand(leftType),
            new PrimitiveOperand(rightType));

    public static PrimitiveOperatorResult ResolveBinary(
        BinaryOperator binaryOperator,
        PrimitiveOperand left,
        PrimitiveOperand right)
    {
        if (TryResolvePointerArithmetic(binaryOperator, left, right, out var pointerResult))
        {
            return pointerResult;
        }

        if (!PrimitiveTypeRegistry.TryGet(left.Type, out var leftDescriptor)
            || !PrimitiveTypeRegistry.TryGet(right.Type, out var rightDescriptor))
        {
            return new();
        }

        if (binaryOperator is not BinaryOperator.Add
            and not BinaryOperator.Subtract
            and not BinaryOperator.Multiply
            and not BinaryOperator.Divide
            and not BinaryOperator.Modulo)
        {
            return new();
        }

        PromoteCharacter(ref left, ref leftDescriptor);
        PromoteCharacter(ref right, ref rightDescriptor);

        var symbol = binaryOperator.ToSourceText();
        if (!leftDescriptor.IsNumeric || !rightDescriptor.IsNumeric)
        {
            return Unsupported(
                $"Operator '{symbol}' is not defined for primitive operands " +
                $"'{TypeRefFormatter.ToCxString(left.Type)}' and '{TypeRefFormatter.ToCxString(right.Type)}'.");
        }

        if (binaryOperator == BinaryOperator.Modulo
            && (!leftDescriptor.IsInteger || !rightDescriptor.IsInteger))
        {
            return Unsupported(
                $"Operator '%' requires integer operands, but received " +
                $"'{TypeRefFormatter.ToCxString(left.Type)}' and '{TypeRefFormatter.ToCxString(right.Type)}'.");
        }

        if (TryAdaptIntegerLiteral(left, leftDescriptor, right, rightDescriptor, out var literalResultType)
            || TryAdaptIntegerLiteral(right, rightDescriptor, left, leftDescriptor, out literalResultType))
        {
            return Supported(literalResultType);
        }

        if ((IntegerLiteralFailure(left, leftDescriptor, right, rightDescriptor)
            ?? IntegerLiteralFailure(right, rightDescriptor, left, leftDescriptor)) is { } literalFailure)
        {
            return Unsupported(literalFailure);
        }

        if (TypeIdentity.ResolvedEquals(left.Type, right.Type))
        {
            return Supported(left.Type);
        }

        if (leftDescriptor.Category == PrimitiveTypeCategory.FloatingPoint
            || rightDescriptor.Category == PrimitiveTypeCategory.FloatingPoint)
        {
            return ResolveFloatingPoint(left, leftDescriptor, right, rightDescriptor);
        }

        if (leftDescriptor.IsSigned != rightDescriptor.IsSigned)
        {
            return Unsupported(
                $"Operator '{symbol}' cannot implicitly combine signed type " +
                $"'{TypeRefFormatter.ToCxString(left.Type)}' and unsigned type " +
                $"'{TypeRefFormatter.ToCxString(right.Type)}'. Use an explicit cast to select the intended result type.");
        }

        if (leftDescriptor.Rank == rightDescriptor.Rank)
        {
            return Unsupported(
                $"Operator '{symbol}' cannot implicitly combine distinct primitive types " +
                $"'{TypeRefFormatter.ToCxString(left.Type)}' and '{TypeRefFormatter.ToCxString(right.Type)}'. " +
                "Use an explicit cast to select the intended result type.");
        }

        var leftIsWider = leftDescriptor.Rank > rightDescriptor.Rank;
        var resultType = leftIsWider ? left.Type : right.Type;
        return Supported(resultType);
    }

    private static PrimitiveOperatorResult ResolveFloatingPoint(
        PrimitiveOperand left,
        PrimitiveTypeDescriptor leftDescriptor,
        PrimitiveOperand right,
        PrimitiveTypeDescriptor rightDescriptor)
    {
        TypeRef resultType;
        if (leftDescriptor.Category == PrimitiveTypeCategory.FloatingPoint
            && rightDescriptor.Category == PrimitiveTypeCategory.FloatingPoint)
        {
            resultType = leftDescriptor.Rank >= rightDescriptor.Rank ? left.Type : right.Type;
        }
        else
        {
            resultType = leftDescriptor.Category == PrimitiveTypeCategory.FloatingPoint
                ? left.Type
                : right.Type;
        }

        return Supported(resultType);
    }

    private static bool TryAdaptIntegerLiteral(
        PrimitiveOperand literalOperand,
        PrimitiveTypeDescriptor literalDescriptor,
        PrimitiveOperand targetOperand,
        PrimitiveTypeDescriptor targetDescriptor,
        out TypeRef resultType)
    {
        resultType = null!;
        if (literalOperand.IntegerLiteral is not { } value
            || !literalDescriptor.IsInteger
            || !targetDescriptor.IsInteger
            || !CanRepresent(targetDescriptor, value))
        {
            return false;
        }

        resultType = targetOperand.Type;
        return true;
    }

    private static bool CanRepresent(
        PrimitiveTypeDescriptor descriptor,
        BigInteger value)
    {
        if (descriptor.Category == PrimitiveTypeCategory.UnsignedInteger && value < 0)
        {
            return false;
        }

        if (descriptor.BitWidth is not { } bits)
        {
            return true;
        }

        if (descriptor.Category == PrimitiveTypeCategory.UnsignedInteger)
        {
            return value < (BigInteger.One << bits);
        }

        var limit = BigInteger.One << (bits - 1);
        return value >= -limit && value < limit;
    }

    private static string? IntegerLiteralFailure(
        PrimitiveOperand literalOperand,
        PrimitiveTypeDescriptor literalDescriptor,
        PrimitiveOperand targetOperand,
        PrimitiveTypeDescriptor targetDescriptor)
    {
        if (literalOperand.IntegerLiteral is not { } value
            || !literalDescriptor.IsInteger
            || !targetDescriptor.IsInteger
            || CanRepresent(targetDescriptor, value))
        {
            return null;
        }

        return $"Integer literal '{value}' cannot be represented by " +
            $"'{TypeRefFormatter.ToCxString(targetOperand.Type)}'. Use an explicit cast or a wider type.";
    }

    private static void PromoteCharacter(
        ref PrimitiveOperand operand,
        ref PrimitiveTypeDescriptor descriptor)
    {
        if (descriptor.Category != PrimitiveTypeCategory.Character
            || !PrimitiveTypeRegistry.TryGet(TypeRef.Int, out var integerDescriptor))
        {
            return;
        }

        operand = operand with { Type = TypeRef.Int };
        descriptor = integerDescriptor;
    }

    private static PrimitiveOperatorResult Supported(TypeRef resultType) =>
        new(resultType);

    private static PrimitiveOperatorResult Unsupported(string failure) =>
        new(Failure: failure);

    private static bool TryResolvePointerArithmetic(
        BinaryOperator binaryOperator,
        PrimitiveOperand left,
        PrimitiveOperand right,
        out PrimitiveOperatorResult result)
    {
        var leftType = TypeRefFacts.UnwrapAlias(left.Type);
        var rightType = TypeRefFacts.UnwrapAlias(right.Type);
        var leftIsPointer = leftType is TypeRef.Pointer;
        var rightIsPointer = rightType is TypeRef.Pointer;
        var leftIsInteger = PrimitiveTypeRegistry.TryGet(left.Type, out var leftDescriptor)
            && leftDescriptor.IsInteger;
        var rightIsInteger = PrimitiveTypeRegistry.TryGet(right.Type, out var rightDescriptor)
            && rightDescriptor.IsInteger;

        TypeRef? resultType = binaryOperator switch
        {
            BinaryOperator.Add when leftIsPointer && rightIsInteger => left.Type,
            BinaryOperator.Add when leftIsInteger && rightIsPointer => right.Type,
            BinaryOperator.Subtract when leftIsPointer && rightIsInteger => left.Type,
            _ => null,
        };
        if (resultType is null)
        {
            result = null!;
            return false;
        }

        result = Supported(resultType);
        return true;
    }
}
