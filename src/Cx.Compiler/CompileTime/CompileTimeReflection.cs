using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal interface ICompileTimeReflection
{
    bool IsAvailable { get; }

    bool TryGetFields(TypeRef type, out IReadOnlyList<ResolvedField> fields);

    bool TryGetMethods(TypeRef type, out IReadOnlyList<ResolvedMethod> methods);

    bool TryGetEnumType(string name, out TypeRef type);

    bool TryGetEnumMembers(TypeRef type, out IReadOnlyList<ReflectedEnumMember> members);

    bool TryGetEnumDataFields(TypeRef type, out IReadOnlyList<ReflectedEnumDataField> fields);

    bool TryGetModule(string name, out ReflectedModule module);

    bool TryGetModuleForFile(string path, out ReflectedModule module);

    bool TryGetOwnerType(FunctionNode function, out TypeRef ownerType);

    bool TryGetType(SyntaxNode syntax, out TypeRef type);

    bool TryGetAttributes(
        SyntaxNode syntax,
        out IReadOnlyList<AttributeApplicationNode> attributes);

    bool TryGetAttributeDeclaration(
        string name,
        out AttributeDeclarationNode declaration);

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

internal sealed record ReflectedModule(
    string Name,
    IReadOnlyList<SyntaxNode> Functions,
    IReadOnlyList<ReflectedModuleType> Types);

internal sealed record ReflectedModuleType(
    TypeRef Type,
    TopLevelNode Declaration);

internal sealed record ReflectedEnumMember(
    TypeRef EnumType,
    EnumNode Enum,
    EnumMemberNode Declaration,
    int Index,
    IReadOnlyDictionary<string, ExpressionNode> Metadata);

internal sealed record ReflectedEnumDataField(
    TypeRef EnumType,
    EnumNode Enum,
    EnumDataFieldNode Declaration,
    int Index,
    TypeRef Type);

internal sealed class UnavailableCompileTimeReflection : ICompileTimeReflection
{
    public static UnavailableCompileTimeReflection Instance { get; } = new();

    private UnavailableCompileTimeReflection()
    {
    }

    public bool IsAvailable => false;

    public bool TryGetFields(TypeRef type, out IReadOnlyList<ResolvedField> fields)
    {
        fields = [];
        return false;
    }

    public bool TryGetMethods(TypeRef type, out IReadOnlyList<ResolvedMethod> methods)
    {
        methods = [];
        return false;
    }

    public bool TryGetEnumType(string name, out TypeRef type)
    {
        type = new TypeRef.Unknown();
        return false;
    }

    public bool TryGetEnumMembers(TypeRef type, out IReadOnlyList<ReflectedEnumMember> members)
    {
        members = [];
        return false;
    }

    public bool TryGetEnumDataFields(TypeRef type, out IReadOnlyList<ReflectedEnumDataField> fields)
    {
        fields = [];
        return false;
    }

    public bool TryGetModule(string name, out ReflectedModule module)
    {
        module = null!;
        return false;
    }

    public bool TryGetModuleForFile(string path, out ReflectedModule module)
    {
        module = null!;
        return false;
    }

    public bool TryGetOwnerType(FunctionNode function, out TypeRef ownerType)
    {
        ownerType = new TypeRef.Unknown();
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

    public bool TryGetAttributeDeclaration(
        string name,
        out AttributeDeclarationNode declaration)
    {
        declaration = null!;
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
    private readonly TypeSystem _typeSystem;
    private readonly IReadOnlyDictionary<string, string> _moduleNamesByPath;

    public ProgramCompileTimeReflection(
        ProgramNode program,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
    {
        _program = program;
        _typeRefParser = new TypeRefParser(program);
        _requirementMatcher = new RequirementMatcher(program);
        _typeSystem = new TypeSystem(program);
        _moduleNamesByPath = moduleNamesByPath
            ?? BuildFallbackModuleMap(program);
    }

    public bool TryGetModule(string name, out ReflectedModule module)
    {
        if (!_moduleNamesByPath.Values.Contains(name, StringComparer.Ordinal))
        {
            module = null!;
            return false;
        }

        module = BuildModule(name);
        return true;
    }

    public bool TryGetModuleForFile(string path, out ReflectedModule module)
    {
        if (!_moduleNamesByPath.TryGetValue(path, out var moduleName))
        {
            module = null!;
            return false;
        }

        module = BuildModule(moduleName);
        return true;
    }

    public bool IsAvailable => true;

    public bool TryGetFields(TypeRef type, out IReadOnlyList<ResolvedField> fields)
    {
        var resolved = _typeSystem.ResolveDefinition(type);
        if (resolved.Symbol is not TypeSymbol.Struct)
        {
            fields = [];
            return false;
        }

        fields = _typeSystem.GetFields(resolved);
        return true;
    }

    public bool TryGetMethods(TypeRef type, out IReadOnlyList<ResolvedMethod> methods)
    {
        var resolved = _typeSystem.ResolveDefinition(type);
        if (resolved.Symbol is null)
        {
            methods = [];
            return false;
        }

        methods = _typeSystem.GetMethods(resolved);
        return true;
    }

    public bool TryGetEnumType(string name, out TypeRef type)
    {
        var enumNode = _program.Enums.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        if (enumNode is null)
        {
            type = new TypeRef.Unknown();
            return false;
        }

        type = new TypeRef.Named(enumNode.Name, [], enumNode.Semantic.ModuleName);
        return true;
    }

    public bool TryGetEnumMembers(TypeRef type, out IReadOnlyList<ReflectedEnumMember> members)
    {
        var enumNode = ResolveEnum(type);
        if (enumNode is null)
        {
            members = [];
            return false;
        }

        var fields = enumNode.DataFields ?? [];
        members = enumNode.Members
            .Select((member, index) => new ReflectedEnumMember(
                type,
                enumNode,
                member,
                index,
                BuildEnumMetadata(fields, member)))
            .ToList();
        return true;
    }

    public bool TryGetEnumDataFields(TypeRef type, out IReadOnlyList<ReflectedEnumDataField> fields)
    {
        var enumNode = ResolveEnum(type);
        if (enumNode?.DataFields is null)
        {
            fields = [];
            return false;
        }

        fields = enumNode.DataFields
            .Select((field, index) => new ReflectedEnumDataField(
                type,
                enumNode,
                field,
                index,
                _typeRefParser.Parse(field.TypeNode)))
            .ToList();
        return true;
    }

    private EnumNode? ResolveEnum(TypeRef type)
    {
        var named = TypeRefFacts.UnwrapConst(TypeRefFacts.UnwrapAlias(type)) as TypeRef.Named;
        if (named is null)
        {
            return null;
        }

        var qualifiedName = named.ModuleName is null ? null : $"{named.ModuleName}.{named.Name}";
        return _program.Enums.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, named.Name, StringComparison.Ordinal)
            || qualifiedName is not null && string.Equals(candidate.Name, qualifiedName, StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, ExpressionNode> BuildEnumMetadata(
        IReadOnlyList<EnumDataFieldNode> fields,
        EnumMemberNode member)
    {
        var metadata = new Dictionary<string, ExpressionNode>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var value = member.DataValues?.FirstOrDefault(candidate => candidate.Name == field.Name)?.Value
                ?? field.DefaultValue;
            if (value is not null)
            {
                metadata[field.Name] = value;
            }
        }

        return metadata;
    }

    public bool TryGetOwnerType(FunctionNode function, out TypeRef ownerType)
    {
        if (function.OwnerTypeNode is null)
        {
            ownerType = new TypeRef.Unknown();
            return false;
        }

        ownerType = _typeRefParser.Parse(function.OwnerTypeNode);
        return ownerType is not TypeRef.Unknown;
    }

    public bool TryGetType(SyntaxNode syntax, out TypeRef type)
    {
        var typeNode = syntax switch
        {
            TypeNode node => node,
            StructFieldNode field => field.TypeNode,
            EnumDataFieldNode field => field.TypeNode,
            TaggedUnionVariantNode variant => variant.TypeNode,
            ParameterNode parameter => parameter.TypeNode,
            GlobalVariableNode global => global.TypeNode,
            TypeAliasNode alias => alias.TargetTypeNode,
            FunctionNode function => function.ReturnTypeNode,
            ExternFunctionNode function => function.ReturnTypeNode,
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

    public bool TryGetAttributeDeclaration(
        string name,
        out AttributeDeclarationNode declaration)
    {
        declaration = _program.AttributeDeclarations.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))!;
        return declaration is not null;
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

    private ReflectedModule BuildModule(string name)
    {
        var functions = _program.Functions
            .Where(function => function.OwnerTypeNode is null && IsInModule(function, name))
            .Cast<SyntaxNode>()
            .Concat(_program.ExternFunctions.Where(function => IsInModule(function, name)))
            .ToList();
        var types = _program.Declarations
            .Where(declaration => IsInModule(declaration, name))
            .Select(declaration => ToReflectedType(declaration, name))
            .OfType<ReflectedModuleType>()
            .ToList();
        return new ReflectedModule(name, functions, types);
    }

    private bool IsInModule(SyntaxNode syntax, string moduleName) =>
        _moduleNamesByPath.TryGetValue(syntax.Location.File.Path, out var declaredModule)
        && string.Equals(declaredModule, moduleName, StringComparison.Ordinal);

    private static ReflectedModuleType? ToReflectedType(
        TopLevelNode declaration,
        string moduleName)
    {
        var (name, typeParameters) = declaration switch
        {
            StructNode node => (node.Name, node.TypeParameters),
            TypeAdapterNode node => (node.Name, node.TypeParameters),
            TypeAliasNode node => (node.Name, (IReadOnlyList<string>)[]),
            EnumNode node => (node.Name, (IReadOnlyList<string>)[]),
            InterfaceNode node => (node.Name, (IReadOnlyList<string>)[]),
            TaggedUnionNode node => (node.Name, (IReadOnlyList<string>)[]),
            _ => (string.Empty, (IReadOnlyList<string>)[]),
        };
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var arguments = typeParameters
            .Select(parameter => (TypeRef)new TypeRef.Named(parameter, []))
            .ToList();
        return new ReflectedModuleType(
            new TypeRef.Named(name, arguments, moduleName),
            declaration);
    }

    private static IReadOnlyDictionary<string, string> BuildFallbackModuleMap(ProgramNode program)
    {
        var moduleName = program.Module?.Name ?? string.Empty;
        return program.Declarations
            .Select(declaration => declaration.Location.File.Path)
            .Append(program.Location.File.Path)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, _ => moduleName, StringComparer.Ordinal);
    }
}
