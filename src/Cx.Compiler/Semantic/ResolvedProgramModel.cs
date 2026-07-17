using Cx.Compiler.C;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal enum ResolvedTypeKind
{
    Struct,
    Enum,
    TaggedUnion,
    Alias,
    ExternalAlias,
}

internal sealed record ResolvedProgramModel(
    IReadOnlyList<ResolvedTypeEntity> Types,
    IReadOnlyList<ResolvedFunctionEntity> Functions,
    IReadOnlyList<ResolvedFunctionEntity> ExternFunctions,
    IReadOnlyList<TypeAdapterNode> TypeAdapters)
{
    private readonly IReadOnlyDictionary<string, ResolvedTypeEntity> _typesByKey = Types
        .GroupBy(type => TypeKey(type.Type), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, ResolvedTypeEntity> _typesByShape = Types
        .GroupBy(type => TypeShapeKey(type.Type), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    public static ResolvedProgramModel Build(
        ProgramNode program,
        CNameManglerOptions? nameManglerOptions = null)
    {
        var parser = new TypeRefParser(program);
        var abiNames = new CAbiNameService(program.TypeAdapters);
        var nameMangler = new CNameMangler(
            abiNames.SpecializationTypeName,
            CTypeLowerer.SanitizeTypeName,
            nameManglerOptions,
            nameManglerOptions is null
                ? CNameMangler.FindModuleCollisionKeys(program.Functions)
                : null);

        var types = BuildTypes(program, parser).ToList();
        var typeMethods = new Dictionary<string, List<ResolvedFunctionEntity>>(StringComparer.Ordinal);
        var typeStaticMethods = new Dictionary<string, List<ResolvedFunctionEntity>>(StringComparer.Ordinal);
        var functions = new List<ResolvedFunctionEntity>();

        foreach (var function in program.Functions)
        {
            var resolvedFunction = ResolveFunction(function, parser, nameMangler);
            if (resolvedFunction.ReceiverType is { } receiverType)
            {
                var target = resolvedFunction.IsStatic ? typeStaticMethods : typeMethods;
                AddByTypeKey(target, receiverType, resolvedFunction);
            }
            else
            {
                functions.Add(resolvedFunction);
            }
        }

        foreach (var structNode in program.Structs)
        {
            AddOwnedFunctions(
                DeclarationType(structNode.Name, structNode.TypeParameters, structNode.Semantic.ModuleName),
                structNode.Methods,
                parser,
                nameMangler,
                typeMethods,
                typeStaticMethods);
        }

        foreach (var extension in program.Extensions)
        {
            if (extension.TargetTypeNode is null)
            {
                continue;
            }

            AddOwnedFunctions(
                ExtensionTargetType(extension, parser),
                extension.Methods,
                parser,
                nameMangler,
                typeMethods,
                typeStaticMethods);
        }

        foreach (var union in program.TaggedUnions)
        {
                AddOwnedFunctions(union.Name, union.Methods, parser, nameMangler, typeMethods, typeStaticMethods);
        }

        var typesWithMethods = types
            .Select(type =>
            {
                var key = TypeKey(type.Type);
                return type with
                {
                    Methods = typeMethods.GetValueOrDefault(key) ?? [],
                    StaticMethods = typeStaticMethods.GetValueOrDefault(key) ?? [],
                };
            })
            .ToList();

        var externFunctions = program.ExternFunctions
            .Concat(program.CDeclarations.SelectMany(declaration => declaration.Functions))
            .Select(function => ResolveExternFunction(function, parser))
            .ToList();

        return new ResolvedProgramModel(typesWithMethods, functions, externFunctions, program.TypeAdapters);
    }

    public bool TryGetType(TypeRef type, out ResolvedTypeEntity entity)
    {
        if (_typesByKey.TryGetValue(TypeKey(type), out entity!))
        {
            return true;
        }

        if (TypeRefFacts.UnwrapAlias(type) is TypeRef.Named named
            && named.Arguments.Count > 0
            && _typesByShape.TryGetValue(TypeShapeKey(type), out var genericDefinition))
        {
            entity = genericDefinition with
            {
                Type = type,
                CName = CTypeLowerer.LowerType(type, TypeAdapters),
            };
            return true;
        }

        return false;
    }

    public bool TryGetMethod(TypeRef receiverType, string name, out ResolvedFunctionEntity method) =>
        TryGetType(receiverType, out var entity)
            ? TryFindFunction(entity.Methods, name, out method)
            : MissingFunction(out method);

    public bool TryGetStaticMethod(TypeRef receiverType, string name, out ResolvedFunctionEntity method) =>
        TryGetType(receiverType, out var entity)
            ? TryFindFunction(entity.StaticMethods, name, out method)
            : MissingFunction(out method);

    private static IEnumerable<ResolvedTypeEntity> BuildTypes(ProgramNode program, TypeRefParser parser)
    {
        foreach (var alias in program.TypeAliases)
        {
            yield return ResolveAlias(alias, parser, ResolvedTypeKind.Alias, program.TypeAdapters);
        }

        foreach (var alias in program.CDeclarations.SelectMany(declaration => declaration.TypeAliases))
        {
            yield return ResolveAlias(alias, parser, ResolvedTypeKind.ExternalAlias, program.TypeAdapters);
        }

        foreach (var structNode in program.Structs.Concat(program.CDeclarations.SelectMany(declaration => declaration.Structs)))
        {
            yield return new ResolvedTypeEntity(
                DeclarationType(structNode.Name, structNode.TypeParameters, structNode.Semantic.ModuleName),
                structNode.Name,
                CTypeLowerer.LowerType(DeclarationType(structNode.Name, structNode.TypeParameters, structNode.Semantic.ModuleName), program.TypeAdapters),
                ResolvedTypeKind.Struct,
                structNode,
                Methods: [],
                StaticMethods: [],
                EnumMembers: []);
        }

        foreach (var enumNode in program.Enums.Concat(program.CDeclarations.SelectMany(declaration => declaration.Enums)))
        {
            var type = new TypeRef.Named(enumNode.Name, [], enumNode.Semantic.ModuleName);
            yield return new ResolvedTypeEntity(
                type,
                enumNode.Name,
                CTypeLowerer.LowerType(type, program.TypeAdapters),
                ResolvedTypeKind.Enum,
                enumNode,
                Methods: [],
                StaticMethods: [],
                EnumMembers: enumNode.Members
                    .Select(member => new ResolvedEnumMemberEntity(member.Name, member.Name, member))
                    .ToList());
        }

        foreach (var union in program.TaggedUnions.Concat(program.CDeclarations.SelectMany(declaration => declaration.Unions)))
        {
            var type = new TypeRef.Named(union.Name, [], union.Semantic.ModuleName);
            yield return new ResolvedTypeEntity(
                type,
                union.Name,
                CTypeLowerer.LowerType(type, program.TypeAdapters),
                ResolvedTypeKind.TaggedUnion,
                union,
                Methods: [],
                StaticMethods: [],
                EnumMembers: []);
        }
    }

    private static ResolvedTypeEntity ResolveAlias(
        TypeAliasNode alias,
        TypeRefParser parser,
        ResolvedTypeKind kind,
        IReadOnlyList<TypeAdapterNode> typeAdapters)
    {
        var type = new TypeRef.Named(alias.Name, [], alias.Semantic.ModuleName);
        return new ResolvedTypeEntity(
            type,
            alias.Name,
            CTypeLowerer.LowerType(type, typeAdapters),
            kind,
            alias,
            Methods: [],
            StaticMethods: [],
            EnumMembers: []);
    }

    private static TypeRef ExtensionTargetType(ExtensionNode extension, TypeRefParser parser)
    {
        if (extension.TargetTypeNode is null)
        {
            return new TypeRef.Unknown();
        }

        var targetType = extension.TargetTypeNode.ToTypeRef(parser);
        if (extension.TypeParameters.Count == 0 || targetType is not TypeRef.Named named)
        {
            return targetType;
        }

        return named with
        {
            Arguments = extension.TypeParameters
                .Select(parameter => new TypeRef.Named(parameter, []))
                .ToList(),
        };
    }

    private static void AddOwnedFunctions(
        string ownerTypeName,
        IReadOnlyList<FunctionNode> functions,
        TypeRefParser parser,
        CNameMangler nameMangler,
        Dictionary<string, List<ResolvedFunctionEntity>> methods,
        Dictionary<string, List<ResolvedFunctionEntity>> staticMethods) =>
        AddOwnedFunctions(
            new TypeRef.Named(ownerTypeName, []),
            functions,
            parser,
            nameMangler,
            methods,
            staticMethods);

    private static void AddOwnedFunctions(
        TypeRef ownerType,
        IReadOnlyList<FunctionNode> functions,
        TypeRefParser parser,
        CNameMangler nameMangler,
        Dictionary<string, List<ResolvedFunctionEntity>> methods,
        Dictionary<string, List<ResolvedFunctionEntity>> staticMethods)
    {
        foreach (var function in functions)
        {
            var resolvedFunction = ResolveFunction(function, parser, nameMangler, ownerType);
            AddByTypeKey(
                resolvedFunction.IsStatic ? staticMethods : methods,
                ownerType,
                resolvedFunction);
        }
    }

    private static ResolvedFunctionEntity ResolveFunction(
        FunctionNode function,
        TypeRefParser parser,
        CNameMangler nameMangler,
        TypeRef? fallbackReceiverType = null)
    {
        var receiverType = fallbackReceiverType is not null
            ? fallbackReceiverType
            : function.OwnerTypeNode is not null
            ? function.OwnerTypeNode.ToTypeRef(parser)
            : fallbackReceiverType;

        return new ResolvedFunctionEntity(
            function.Name,
            nameMangler.FunctionName(function),
            function,
            ExternFunction: null,
            ReturnType: function.ReturnTypeNode.ToTypeRef(parser),
            Parameters: ResolveParameters(function.Parameters, parser),
            ReceiverType: receiverType,
            IsStatic: function.IsStatic,
            IsExtern: false,
            IsMacro: false,
            IsGeneric: function.TypeParameters.Count > 0 || function.TypeArgumentNodes.Count > 0);
    }

    private static ResolvedFunctionEntity ResolveExternFunction(
        ExternFunctionNode function,
        TypeRefParser parser) =>
        new(
            function.Name,
            function.Name,
            Function: null,
            function,
            ReturnType: function.ReturnTypeNode.ToTypeRef(parser),
            Parameters: ResolveParameters(function.Parameters, parser),
            ReceiverType: null,
            IsStatic: true,
            IsExtern: true,
            function.IsMacro,
            IsGeneric: function.TypeParameters.Count > 0);

    private static IReadOnlyList<ResolvedParameterEntity> ResolveParameters(
        IReadOnlyList<ParameterNode> parameters,
        TypeRefParser parser) =>
        parameters
            .Select(parameter => new ResolvedParameterEntity(
                parameter.Name,
                parameter.TypeNode.ToTypeRef(parser),
                parameter.IsVariadic))
            .ToList();

    private static TypeRef DeclarationType(
        string name,
        IReadOnlyList<string> typeParameters,
        string? moduleName = null) =>
        new TypeRef.Named(
            name,
            typeParameters.Select(parameter => new TypeRef.Named(parameter, [])).ToList(),
            moduleName);

    private static void AddByTypeKey(
        Dictionary<string, List<ResolvedFunctionEntity>> functions,
        TypeRef type,
        ResolvedFunctionEntity function)
    {
        var key = TypeKey(type);
        if (!functions.TryGetValue(key, out var list))
        {
            list = [];
            functions[key] = list;
        }

        list.Add(function);
    }

    private static bool TryFindFunction(
        IReadOnlyList<ResolvedFunctionEntity> functions,
        string name,
        out ResolvedFunctionEntity function)
    {
        function = functions.FirstOrDefault(candidate => string.Equals(candidate.SourceName, name, StringComparison.Ordinal))!;
        return function is not null;
    }

    private static bool MissingFunction(out ResolvedFunctionEntity function)
    {
        function = null!;
        return false;
    }

    private static string TypeKey(TypeRef type) =>
        TypeIdentity.ResolvedKey(type);

    private static string TypeShapeKey(TypeRef type) =>
        TypeRefFacts.UnwrapAlias(type) is TypeRef.Named named
            ? $"{named.ModuleName ?? string.Empty}::{named.Name}/{named.Arguments.Count}"
            : TypeKey(type);
}

internal sealed record ResolvedTypeEntity(
    TypeRef Type,
    string SourceName,
    string CName,
    ResolvedTypeKind Kind,
    SyntaxNode Declaration,
    IReadOnlyList<ResolvedFunctionEntity> Methods,
    IReadOnlyList<ResolvedFunctionEntity> StaticMethods,
    IReadOnlyList<ResolvedEnumMemberEntity> EnumMembers);

internal sealed record ResolvedFunctionEntity(
    string SourceName,
    string CName,
    FunctionNode? Function,
    ExternFunctionNode? ExternFunction,
    TypeRef ReturnType,
    IReadOnlyList<ResolvedParameterEntity> Parameters,
    TypeRef? ReceiverType,
    bool IsStatic,
    bool IsExtern,
    bool IsMacro,
    bool IsGeneric);

internal sealed record ResolvedParameterEntity(
    string Name,
    TypeRef Type,
    bool IsVariadic);

internal sealed record ResolvedEnumMemberEntity(
    string SourceName,
    string CName,
    EnumMemberNode Declaration);
