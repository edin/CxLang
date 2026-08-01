using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal sealed class CoreCxValueConversionPass(ProgramNode program)
{
    private readonly TypeRefParser _typeRefParser = new(program);
    private readonly IReadOnlyDictionary<string, InterfaceNode> _interfaces =
        program.Interfaces.ToDictionary(
            interfaceNode => interfaceNode.Name,
            StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, StructNode> _structs =
        program.Structs.ToDictionary(
            structNode => structNode.Name,
            StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, TaggedUnionNode> _taggedUnions =
        program.TaggedUnions.ToDictionary(
            union => union.Name,
            StringComparer.Ordinal);
    private readonly TypeSystem _typeSystem = new(program);

    public void Apply()
    {
        AnnotateGlobals();
        AnnotateDataEnums();
        foreach (var function in program.Functions.Where(candidate =>
                     candidate.TypeParameters.Count == 0))
        {
            AnnotateFunction(function);
        }
    }

    private void AnnotateGlobals()
    {
        foreach (var global in program.GlobalVariables.Where(global =>
                     global.Initializer is not null))
        {
            Annotate(
                global.Initializer!,
                ResolveType(global.TypeNode));
        }
    }

    private void AnnotateDataEnums()
    {
        foreach (var enumNode in program.Enums.Where(node =>
                     node.IsDataEnum))
        {
            var fields = (enumNode.DataFields ?? [])
                .ToDictionary(field => field.Name, StringComparer.Ordinal);
            foreach (var value in enumNode.Members
                         .SelectMany(member => member.DataValues ?? []))
            {
                if (fields.TryGetValue(value.Name, out var field))
                {
                    Annotate(
                        value.Value,
                        ResolveType(field.TypeNode));
                }
            }
        }
    }

    private void AnnotateFunction(FunctionNode function)
    {
        var roots = function.Body
            .SelectMany(AstTraversal.DescendantsAndSelf)
            .ToList();

        foreach (var let in roots.OfType<LetStatement>())
        {
            Annotate(let.Initializer, ResolveType(let.TypeNode));
        }

        foreach (var ret in roots.OfType<ReturnStatement>())
        {
            Annotate(
                ret.Expression,
                ResolveType(function.ReturnTypeNode));
        }

        foreach (var declaration in roots
                     .OfType<ForDeclarationInitializerNode>())
        {
            Annotate(
                declaration.Initializer,
                ResolveType(declaration.TypeNode));
        }

        foreach (var assignment in roots
                     .OfType<AssignmentExpressionNode>()
                     .Where(assignment =>
                         assignment.Operator == AssignmentOperator.Assign))
        {
            Annotate(
                assignment.Value,
                ExpressionType(assignment.Target));
        }

        foreach (var initializer in roots
                     .OfType<InitializerExpressionNode>())
        {
            AnnotateInitializer(initializer);
        }

        foreach (var call in roots.OfType<CallExpressionNode>())
        {
            AnnotateCallArguments(call);
        }
    }

    private void AnnotateInitializer(InitializerExpressionNode initializer)
    {
        if (ResolveType(initializer.TypeNameNode) is not { } targetType)
        {
            return;
        }

        var fields = _typeSystem.GetFields(
                TypeRefFacts.StripPointersAndAliases(targetType))
            .ToDictionary(field => field.Name, StringComparer.Ordinal);
        foreach (var field in initializer.Fields)
        {
            if (fields.TryGetValue(field.Name, out var resolved))
            {
                Annotate(field.Value, resolved.Type);
            }
        }
    }

    private void AnnotateCallArguments(CallExpressionNode call)
    {
        var parameterTypes = CallParameterTypes(call);
        foreach (var (argument, targetType) in call.Arguments.Zip(
                     parameterTypes))
        {
            Annotate(argument, targetType);
        }
    }

    private IReadOnlyList<TypeRef> CallParameterTypes(
        CallExpressionNode call)
    {
        if (call.Semantic.ResolvedCall is { } resolved)
        {
            return resolved.Function.Parameters
                .Skip(resolved.IsInstance ? 1 : 0)
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => ResolveType(parameter.TypeNode)
                    ?? new TypeRef.Unknown())
                .ToList();
        }

        if (call.Semantic.CoreExternCall is { } external)
        {
            return external.Function.Parameters
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => ResolveType(parameter.TypeNode)
                    ?? new TypeRef.Unknown())
                .ToList();
        }

        if (call.Semantic.CoreInterfaceCall is { } interfaceCall)
        {
            return interfaceCall.Method.Parameters
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => ResolveType(parameter.TypeNode)
                    ?? new TypeRef.Unknown())
                .ToList();
        }

        return ExpressionType(call.Callee) is TypeRef.Function function
            ? function.Parameters
            : [];
    }

    private void Annotate(
        ExpressionNode? expression,
        TypeRef? targetType)
    {
        if (expression is null
            || targetType is null
            || targetType is TypeRef.Unknown
            || ExpressionType(expression) is not { } sourceType)
        {
            return;
        }

        expression.Semantic.ValueConversion =
            (CoreValueConversionInfo?)ResolveInterfaceConversion(
                expression,
                targetType,
                sourceType)
            ?? ResolveTaggedUnionConversion(
                targetType,
                sourceType);
    }

    private CoreValueConversionInfo.Interface? ResolveInterfaceConversion(
        ExpressionNode expression,
        TypeRef targetType,
        TypeRef sourceType)
    {
        if (expression is not NameExpressionNode
            || TypeRefFacts.GetBaseName(
                TypeRefFacts.StripPointersAndAliases(targetType)) is not
                { } interfaceName
            || !_interfaces.TryGetValue(
                interfaceName,
                out var interfaceNode))
        {
            return null;
        }

        var sourceIsPointer =
            TypeRefFacts.UnwrapAlias(sourceType) is TypeRef.Pointer;
        var sourceValueType = TypeRefFacts.StripPointersAndAliases(
            sourceType);
        if (TypeRefFacts.GetBaseName(sourceValueType) is not
                { } sourceName
            || !_structs.TryGetValue(
                sourceName,
                out var structNode)
            || !structNode.Semantic.CoreInterfaceImplementations.Any(
                implementation =>
                    ReferenceEquals(
                        implementation.Interface,
                        interfaceNode)))
        {
            return null;
        }

        return new CoreValueConversionInfo.Interface(
            interfaceNode,
            structNode,
            targetType,
            sourceType,
            sourceIsPointer);
    }

    private CoreValueConversionInfo.TaggedUnion?
        ResolveTaggedUnionConversion(
            TypeRef targetType,
            TypeRef sourceType)
    {
        if (TypeRefFacts.GetBaseName(
                TypeRefFacts.StripPointersAndAliases(targetType)) is not
                { } unionName
            || !_taggedUnions.TryGetValue(
                unionName,
                out var union)
            || union.IsRaw)
        {
            return null;
        }

        var normalizedSource = NormalizeStorageType(sourceType);
        var matches = union.Variants
            .Where(variant =>
                ResolveType(variant.TypeNode) is { } variantType
                && TypeIdentity.SpecializationEquals(
                    NormalizeStorageType(variantType),
                    normalizedSource))
            .ToList();
        return matches.Count == 1
            ? new CoreValueConversionInfo.TaggedUnion(
                union,
                matches[0],
                targetType)
            : null;
    }

    private TypeRef NormalizeStorageType(TypeRef type) =>
        TypeAdapterStorageResolver.Resolve(
            TypeRefFacts.StripPointersAndAliases(type),
            program.TypeAdapters);

    private TypeRef? ExpressionType(ExpressionNode expression) =>
        CoreExpressionTypeFacts.TryGet(expression);

    private TypeRef? ResolveType(TypeNode? typeNode)
    {
        var type = typeNode?.Semantic.Type
            ?? typeNode.ToTypeRef(_typeRefParser);
        return type is TypeRef.Unknown ? null : type;
    }
}
