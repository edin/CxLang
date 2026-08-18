using Cx.Compiler.Semantic;
using Cx.Compiler.Lowering;
using Cx.Compiler.Modules;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal interface ICompileTimeReflection
{
    bool IsAvailable { get; }

    bool TryGetFields(TypeRef type, out IReadOnlyList<ResolvedField> fields);

    bool TryGetMethods(TypeRef type, out IReadOnlyList<ResolvedMethod> methods);

    bool TryGetNamedType(string name, out TypeRef type);

    bool TryGetEnumType(string name, out TypeRef type);

    bool TryGetEnumMembers(TypeRef type, out IReadOnlyList<ReflectedEnumMember> members);

    bool TryGetEnumDataFields(TypeRef type, out IReadOnlyList<ReflectedEnumDataField> fields);

    bool TryGetModule(string name, out ReflectedModule module);

    bool TryGetProgram(out ReflectedProgram program);

    bool TryGetModuleForSyntax(
        SyntaxNode syntax,
        out ReflectedModule module);

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
    IReadOnlyList<ReflectedModuleType> Types,
    IReadOnlyList<GlobalVariableNode> Globals,
    IReadOnlyList<CompileTimeConstantNode> Constants,
    IReadOnlyList<InterfaceNode> Interfaces,
    IReadOnlyList<RequirementNode> Requirements,
    IReadOnlyList<AttributeDeclarationNode> AttributeDeclarations,
    IReadOnlyList<AttributeApplicationNode> Attributes);

internal sealed record ReflectedProgram(
    IReadOnlyList<ReflectedModule> Modules);

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

internal sealed record ReflectedEnumDataEntry(
    ReflectedEnumMember Member,
    ReflectedEnumDataField Field,
    ExpressionNode? Value,
    bool IsExplicit);

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

    public bool TryGetNamedType(string name, out TypeRef type)
    {
        type = new TypeRef.Unknown();
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

    public bool TryGetProgram(out ReflectedProgram program)
    {
        program = null!;
        return false;
    }

    public bool TryGetModuleForSyntax(
        SyntaxNode syntax,
        out ReflectedModule module)
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
    private readonly IReadOnlyList<string> _moduleNames;
    private readonly ModuleOwnership _moduleOwnership;
    private readonly ProgramDeclarationIndex _declarations;
    private readonly IReadOnlyList<CompileTimeConstantNode> _compileTimeConstants;

    public ProgramCompileTimeReflection(
        ProgramNode program,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null,
        IReadOnlyList<CompileTimeConstantNode>? compileTimeConstants = null)
    {
        _program = program;
        _compileTimeConstants = compileTimeConstants ?? program.CompileTimeConstants;
        _typeRefParser = new TypeRefParser(program);
        _moduleNamesByPath = moduleNamesByPath
            ?? BuildFallbackModuleMap(program);
        _moduleOwnership = ModuleOwnership.Create(
            program,
            _moduleNamesByPath);
        _moduleNames = program.Declarations
            .Select(_moduleOwnership
                .GetDeclarationModuleName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        _declarations = ProgramDeclarationIndex.Create(
            program,
            _moduleOwnership);
        _requirementMatcher = new RequirementMatcher(
            program,
            _declarations);
        _typeSystem = new TypeSystem(program);
    }

    public bool TryGetModule(string name, out ReflectedModule module)
    {
        if (!_moduleNames.Contains(name))
        {
            module = null!;
            return false;
        }

        module = BuildModule(name);
        return true;
    }

    public bool TryGetProgram(out ReflectedProgram program)
    {
        program = new ReflectedProgram(
            _moduleNames.Select(BuildModule).ToList());
        return true;
    }

    public bool TryGetModuleForSyntax(
        SyntaxNode syntax,
        out ReflectedModule module) =>
        TryGetModule(
            _moduleOwnership.GetModuleName(syntax),
            out module);

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

    public bool TryGetNamedType(string name, out TypeRef type)
    {
        var parsed = _typeRefParser.Parse(name);
        if (parsed is TypeRef.Alias)
        {
            type = parsed;
            return true;
        }

        var lookup = _declarations.LookupTypeFromModule(
            _program.Module?.Name ?? string.Empty,
            new TypeRef.Named(name, []));
        if (lookup is not ProgramTypeDeclarationLookup.Found found)
        {
            type = new TypeRef.Unknown();
            return false;
        }

        type = new TypeRef.Named(name, [], found.ModuleName);
        return true;
    }

    public bool TryGetEnumType(string name, out TypeRef type)
    {
        if (_declarations.LookupFromModule<EnumNode>(
                _program.Module?.Name ?? string.Empty,
                name)
            is not ProgramDeclarationLookup<EnumNode>.Found found)
        {
            type = new TypeRef.Unknown();
            return false;
        }

        var enumNode = found.Declaration;
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
                BuildEnumMetadata(fields, member, index)))
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

        return ResolveNamedDeclaration<EnumNode>(named);
    }

    private static IReadOnlyDictionary<string, ExpressionNode> BuildEnumMetadata(
        IReadOnlyList<EnumDataFieldNode> fields,
        EnumMemberNode member,
        int memberIndex)
    {
        var metadata = new Dictionary<string, ExpressionNode>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var explicitValue = member.DataValues?
                .FirstOrDefault(candidate => candidate.Name == field.Name)?
                .Value;
            var value = explicitValue
                ?? (field.DefaultValue is null
                    ? null
                    : DataEnumDefaultExpressionSpecializer.Specialize(
                        field.DefaultValue,
                        member,
                        memberIndex));
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
            CompileTimeConstantNode constant => constant.TypeNode,
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
            CompileTimeConstantNode constant => constant.Attributes,
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
            ModuleDeclarationNode module => module.Attributes,
            ModuleBlockNode module => module.Attributes,
            _ => [],
        };

        return syntax is TypeAliasNode
            or ExternFunctionNode
            or GlobalVariableNode
            or CompileTimeConstantNode
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
            or TestNode
            or ModuleDeclarationNode
            or ModuleBlockNode;
    }

    public bool TryGetAttributeDeclaration(
        string name,
        out AttributeDeclarationNode declaration)
    {
        if (_declarations.LookupFromModule<AttributeDeclarationNode>(
                _program.Module?.Name ?? string.Empty,
                name)
            is ProgramDeclarationLookup<AttributeDeclarationNode>.Found found)
        {
            declaration = found.Declaration;
            return true;
        }

        declaration = null!;
        return false;
    }

    public bool TryGetRequirement(string name, out RequirementNode requirement)
    {
        if (_declarations.LookupFromModule<RequirementNode>(
                _program.Module?.Name ?? string.Empty,
                name)
            is ProgramDeclarationLookup<RequirementNode>.Found found)
        {
            requirement = found.Declaration;
            return true;
        }

        requirement = null!;
        return false;
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

        structNode = ResolveNamedDeclaration<StructNode>(named)!;
        return structNode is not null;
    }

    private T? ResolveNamedDeclaration<T>(TypeRef.Named named)
        where T : TopLevelNode
    {
        if (named.ModuleName is not null)
        {
            var moduleLookup = _declarations.LookupInModule<T>(
                named.ModuleName,
                named.Name);
            if (moduleLookup is ProgramDeclarationLookup<T>.Found moduleDeclaration)
            {
                return moduleDeclaration.Declaration;
            }

            if (moduleLookup is ProgramDeclarationLookup<T>.Ambiguous)
            {
                return null;
            }
        }

        return _declarations.Lookup<T>(named.Name)
            is ProgramDeclarationLookup<T>.Found declaration
                ? declaration.Declaration
                : null;
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
        var globals = _program.GlobalVariables
            .Where(global => IsInModule(global, name))
            .ToList();
        var constants = _compileTimeConstants
            .Where(constant => IsInModule(constant, name))
            .ToList();
        var interfaces = _program.Interfaces
            .Where(interfaceNode => IsInModule(interfaceNode, name))
            .ToList();
        var requirements = _program.Requirements
            .Where(requirement => IsInModule(requirement, name))
            .ToList();
        var attributeDeclarations = _program.AttributeDeclarations
            .Where(attribute => IsInModule(attribute, name))
            .ToList();
        var attributes = _program.Declarations
            .OfType<ModuleDeclarationNode>()
            .Where(module => string.Equals(module.Name, name, StringComparison.Ordinal))
            .SelectMany(module => module.Attributes)
            .ToList();
        return new ReflectedModule(
            name,
            functions,
            types,
            globals,
            constants,
            interfaces,
            requirements,
            attributeDeclarations,
            attributes);
    }

    private bool IsInModule(SyntaxNode syntax, string moduleName) =>
        string.Equals(
            _moduleOwnership.GetModuleName(syntax),
            moduleName,
            StringComparison.Ordinal);

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
