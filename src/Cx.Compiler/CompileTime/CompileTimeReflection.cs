using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal interface ICompileTimeReflection
{
    bool IsAvailable { get; }

    bool TryGetFields(TypeRef type, out IReadOnlyList<StructFieldNode> fields);

    bool TryGetType(SyntaxNode syntax, out TypeRef type);

    bool TryGetAttributes(
        SyntaxNode syntax,
        out IReadOnlyList<AttributeApplicationNode> attributes);

    bool TryGetRequirement(string name, out RequirementNode requirement);

    bool TryMatchRequirement(
        TypeRef type,
        RequirementNode requirement,
        out RequirementMatch match);

    bool TryDeclaresRequirement(
        TypeRef type,
        RequirementNode requirement,
        out bool declares);
}

internal sealed class UnavailableCompileTimeReflection : ICompileTimeReflection
{
    public static UnavailableCompileTimeReflection Instance { get; } = new();

    private UnavailableCompileTimeReflection()
    {
    }

    public bool IsAvailable => false;

    public bool TryGetFields(TypeRef type, out IReadOnlyList<StructFieldNode> fields)
    {
        fields = [];
        return false;
    }

    public bool TryGetType(SyntaxNode syntax, out TypeRef type)
    {
        type = new TypeRef.Unknown();
        return false;
    }

    public bool TryGetAttributes(
        SyntaxNode syntax,
        out IReadOnlyList<AttributeApplicationNode> attributes)
    {
        attributes = [];
        return false;
    }

    public bool TryGetRequirement(string name, out RequirementNode requirement)
    {
        requirement = null!;
        return false;
    }

    public bool TryMatchRequirement(
        TypeRef type,
        RequirementNode requirement,
        out RequirementMatch match)
    {
        match = null!;
        return false;
    }

    public bool TryDeclaresRequirement(
        TypeRef type,
        RequirementNode requirement,
        out bool declares)
    {
        declares = false;
        return false;
    }
}

internal sealed class ProgramCompileTimeReflection : ICompileTimeReflection
{
    private readonly ProgramNode _program;
    private readonly TypeRefParser _typeRefParser;
    private readonly RequirementMatcher _requirementMatcher;

    public ProgramCompileTimeReflection(ProgramNode program)
    {
        _program = program;
        _typeRefParser = new TypeRefParser(program);
        _requirementMatcher = new RequirementMatcher(program);
    }

    public bool IsAvailable => true;

    public bool TryGetFields(TypeRef type, out IReadOnlyList<StructFieldNode> fields)
    {
        var named = TypeRefFacts.UnwrapConst(TypeRefFacts.UnwrapAlias(type)) as TypeRef.Named;
        if (named is null)
        {
            fields = [];
            return false;
        }

        var qualifiedName = named.ModuleName is null
            ? null
            : $"{named.ModuleName}.{named.Name}";
        var structNode = _program.Structs.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, named.Name, StringComparison.Ordinal)
            || qualifiedName is not null
            && string.Equals(candidate.Name, qualifiedName, StringComparison.Ordinal));
        if (structNode is null)
        {
            fields = [];
            return false;
        }

        fields = structNode.Fields;
        return true;
    }

    public bool TryGetType(SyntaxNode syntax, out TypeRef type)
    {
        var typeNode = syntax switch
        {
            StructFieldNode field => field.TypeNode,
            TaggedUnionVariantNode variant => variant.TypeNode,
            ParameterNode parameter => parameter.TypeNode,
            GlobalVariableNode global => global.TypeNode,
            TypeAliasNode alias => alias.TargetTypeNode,
            _ => null,
        };
        if (typeNode is null)
        {
            type = new TypeRef.Unknown();
            return false;
        }

        type = _typeRefParser.Parse(typeNode);
        return type is not TypeRef.Unknown;
    }

    public bool TryGetAttributes(
        SyntaxNode syntax,
        out IReadOnlyList<AttributeApplicationNode> attributes)
    {
        attributes = syntax switch
        {
            TypeAliasNode alias => alias.Attributes,
            ExternFunctionNode function => function.Attributes,
            GlobalVariableNode global => global.Attributes,
            EnumNode enumNode => enumNode.Attributes,
            EnumMemberNode member => member.Attributes,
            StructNode structNode => structNode.Attributes,
            StructFieldNode field => field.Attributes,
            TaggedUnionNode union => union.Attributes,
            TaggedUnionVariantNode variant => variant.Attributes,
            FunctionNode function => function.Attributes,
            ParameterNode parameter => parameter.Attributes,
            InterfaceNode interfaceNode => interfaceNode.Attributes,
            ExtensionNode extension => extension.Attributes,
            TypeAdapterNode adapter => adapter.Attributes,
            TestNode test => test.Attributes,
            _ => [],
        };

        return syntax is TypeAliasNode
            or ExternFunctionNode
            or GlobalVariableNode
            or EnumNode
            or EnumMemberNode
            or StructNode
            or StructFieldNode
            or TaggedUnionNode
            or TaggedUnionVariantNode
            or FunctionNode
            or ParameterNode
            or InterfaceNode
            or ExtensionNode
            or TypeAdapterNode
            or TestNode;
    }

    public bool TryGetRequirement(string name, out RequirementNode requirement)
    {
        requirement = _program.Requirements.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))!;
        return requirement is not null;
    }

    public bool TryMatchRequirement(
        TypeRef type,
        RequirementNode requirement,
        out RequirementMatch match)
    {
        IReadOnlyList<TypeRef>? declaredArguments = null;
        if (TryResolveStruct(type, out var structNode, out var namedType)
            && structNode.Requirements.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, requirement.Name, StringComparison.Ordinal)) is { } declaration)
        {
            var substitutions = structNode.TypeParameters
                .Zip(namedType.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            declaredArguments = declaration.TypeArgumentNodes
                .Select(argument => SubstituteType(_typeRefParser.Parse(argument), substitutions))
                .ToList();
        }

        match = _requirementMatcher.MatchTypeRefs(type, requirement.Name, declaredArguments);
        return true;
    }

    public bool TryDeclaresRequirement(
        TypeRef type,
        RequirementNode requirement,
        out bool declares)
    {
        if (!TryResolveStruct(type, out var structNode, out _))
        {
            declares = false;
            return false;
        }

        declares = structNode.Requirements.Any(candidate =>
            string.Equals(candidate.Name, requirement.Name, StringComparison.Ordinal));
        return true;
    }

    private bool TryResolveStruct(
        TypeRef type,
        out StructNode structNode,
        out TypeRef.Named named)
    {
        named = TypeRefFacts.UnwrapConst(TypeRefFacts.UnwrapAlias(type)) as TypeRef.Named ?? null!;
        if (named is null)
        {
            structNode = null!;
            return false;
        }

        var qualifiedName = named.ModuleName is null ? null : $"{named.ModuleName}.{named.Name}";
        var resolvedName = named.Name;
        structNode = _program.Structs.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, resolvedName, StringComparison.Ordinal)
            || qualifiedName is not null
            && string.Equals(candidate.Name, qualifiedName, StringComparison.Ordinal))!;
        return structNode is not null;
    }

    private static TypeRef SubstituteType(
        TypeRef type,
        IReadOnlyDictionary<string, TypeRef> substitutions) =>
        type switch
        {
            TypeRef.Named { Arguments.Count: 0 } named when substitutions.TryGetValue(named.Name, out var replacement) =>
                replacement,
            TypeRef.Named named => named with
            {
                Arguments = named.Arguments.Select(argument => SubstituteType(argument, substitutions)).ToList(),
            },
            TypeRef.Alias alias => alias with { Target = SubstituteType(alias.Target, substitutions) },
            TypeRef.Pointer pointer => new TypeRef.Pointer(SubstituteType(pointer.Element, substitutions)),
            TypeRef.Const constType => new TypeRef.Const(SubstituteType(constType.Element, substitutions)),
            TypeRef.FixedArray array => new TypeRef.FixedArray(SubstituteType(array.Element, substitutions), array.Length),
            TypeRef.Function function => new TypeRef.Function(
                function.Parameters.Select(parameter => SubstituteType(parameter, substitutions)).ToList(),
                SubstituteType(function.ReturnType, substitutions),
                function.IsVariadic),
            _ => type,
        };
}
