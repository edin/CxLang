using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal sealed record ResolvedMethodCall(
    string DisplayName,
    ResolvedMethod Method,
    bool SkipSelf);

internal sealed class MethodCallResolver(ProgramNode program, TypeSystem typeSystem)
{
    public ResolvedMethodCall? ResolveTypeRefs(
        MemberExpressionNode member,
        IReadOnlyList<TypeRef> typeArguments,
        int argumentCount,
        TypeEnvironment variables)
    {
        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is null)
        {
            return null;
        }

        if (!variables.TryGet(targetName, out var targetType))
        {
            var staticReceiverType = BuildStaticReceiverType(targetName, typeArguments);
            return typeSystem.FindMethod(staticReceiverType, member.MemberName, isStatic: true, argumentCount) is { } staticMethod
                ? new ResolvedMethodCall($"{TypeRefFormatter.ToCxString(staticReceiverType)}.{member.MemberName}", staticMethod, SkipSelf: false)
                : null;
        }

        var instanceReceiverType = NormalizeInstanceReceiverType(targetType);
        return instanceReceiverType is not null
            && typeSystem.FindMethod(instanceReceiverType, member.MemberName, isStatic: false, argumentCount) is { } instanceMethod
            ? new ResolvedMethodCall($"{TypeRefFormatter.ToCxString(instanceReceiverType)}.{member.MemberName}", instanceMethod, SkipSelf: true)
            : null;
    }

    private TypeRef BuildStaticReceiverType(string targetName, IReadOnlyList<TypeRef> typeArguments)
    {
        if (typeArguments.Count == 0)
        {
            return new TypeRef.Named(targetName, []);
        }

        var typeParameterCount = program.Structs
            .FirstOrDefault(structNode => string.Equals(structNode.Name, targetName, StringComparison.Ordinal))
            ?.TypeParameters.Count
            ?? program.TypeAdapters
                .FirstOrDefault(adapter => string.Equals(adapter.Name, targetName, StringComparison.Ordinal))
                ?.TypeParameters.Count
            ?? 0;
        return new TypeRef.Named(
            targetName,
            typeParameterCount == typeArguments.Count ? typeArguments : []);
    }

    private static TypeRef? NormalizeInstanceReceiverType(TypeRef type) =>
        type is TypeRef.Unknown ? null : TypeRefFacts.StripPointersAndAliases(type);

}
