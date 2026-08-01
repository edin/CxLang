using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal static class CoreCxReferenceAnnotationPass
{
    public static void Apply(ProgramNode program)
    {
        var symbolAliases = program.SymbolImports
            .SelectMany(import => import.Symbols)
            .Where(symbol => symbol.Alias is not null)
            .ToDictionary(
                symbol => symbol.Alias!,
                symbol => symbol.Name,
                StringComparer.Ordinal);
        AnnotateLinkedDeclarations(program, symbolAliases);
        var typeRefParser = new TypeRefParser(program);

        var enums = program.Enums.ToDictionary(
            enumNode => enumNode.Name,
            StringComparer.Ordinal);
        var taggedUnions = program.TaggedUnions.ToDictionary(
            union => union.Name,
            StringComparer.Ordinal);
        var structs = program.Structs.ToDictionary(
            structNode => structNode.Name,
            StringComparer.Ordinal);
        var interfaces = program.Interfaces.ToDictionary(
            interfaceNode => interfaceNode.Name,
            StringComparer.Ordinal);
        var moduleQualifiers = program.Imports
            .Select(import => import.Alias ?? import.ModuleName)
            .ToHashSet(StringComparer.Ordinal);
        var linkedDeclarations = program.GlobalVariables
            .Cast<TopLevelNode>()
            .Concat(program.ExternFunctions)
            .GroupBy(
                DeclarationName,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);

        var coreFunctions = program.Functions.Where(function =>
            function.TypeParameters.Count == 0);
        var roots = ExecutableAstTraversal.GetRoots(
                program,
                coreFunctions)
            .ToList();
        foreach (var member in ExecutableAstTraversal
                     .DescendantsAndSelf<MemberExpressionNode>(roots))
        {
            AnnotateMember(
                member,
                enums,
                taggedUnions,
                structs,
                interfaces,
                program.TypeAdapters,
                typeRefParser,
                moduleQualifiers,
                linkedDeclarations);
        }

        foreach (var call in ExecutableAstTraversal
                     .DescendantsAndSelf<CallExpressionNode>(roots))
        {
            if (call.Callee is NameExpressionNode name
                && call.Semantic.CoreDirectCall is { } directCall)
            {
                name.Semantic.CoreFunctionReference =
                    new CoreFunctionReferenceInfo(
                        directCall.Function);
            }
        }

        foreach (var name in ExecutableAstTraversal
                     .DescendantsAndSelf<NameExpressionNode>(roots))
        {
            AnnotateName(
                name,
                linkedDeclarations,
                symbolAliases);
        }
    }

    public static void AnnotateLinkedDeclarations(
        ProgramNode program)
    {
        var symbolAliases = program.SymbolImports
            .SelectMany(import => import.Symbols)
            .Where(symbol => symbol.Alias is not null)
            .ToDictionary(
                symbol => symbol.Alias!,
                symbol => symbol.Name,
                StringComparer.Ordinal);
        AnnotateLinkedDeclarations(program, symbolAliases);
    }

    private static void AnnotateLinkedDeclarations(
        ProgramNode program,
        IReadOnlyDictionary<string, string> symbolAliases)
    {
        foreach (var declaration in program.GlobalVariables
                     .Cast<TopLevelNode>()
                     .Concat(program.ExternFunctions))
        {
            var name = DeclarationName(declaration);
            var linkName = symbolAliases.GetValueOrDefault(
                name,
                UnqualifiedName(name));
            declaration.Semantic.CoreSymbol =
                new CoreSymbolInfo(linkName);
        }
    }

    private static void AnnotateMember(
        MemberExpressionNode member,
        IReadOnlyDictionary<string, EnumNode> enums,
        IReadOnlyDictionary<string, TaggedUnionNode> taggedUnions,
        IReadOnlyDictionary<string, StructNode> structs,
        IReadOnlyDictionary<string, InterfaceNode> interfaces,
        IReadOnlyList<TypeAdapterNode> typeAdapters,
        TypeRefParser typeRefParser,
        IReadOnlySet<string> moduleQualifiers,
        IReadOnlyDictionary<string, TopLevelNode> linkedDeclarations)
    {
        if (TryGetInterfaceTypeIdReference(
                member,
                interfaces) is { } interfaceTypeId)
        {
            member.Semantic.MemberReference = interfaceTypeId;
            return;
        }

        if (ExpressionNameFacts.GetQualifiedName(member.Target) is not
            { } targetName)
        {
            return;
        }

        if (enums.TryGetValue(targetName, out var enumNode)
            && enumNode.Members.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    member.MemberName,
                    StringComparison.Ordinal)) is { } enumMember)
        {
            member.Semantic.MemberReference =
                new CoreMemberReferenceInfo.EnumMember(
                    enumNode,
                    enumMember);
            return;
        }

        if (moduleQualifiers.Contains(targetName))
        {
            var qualifiedName = $"{targetName}.{member.MemberName}";
            var symbol = linkedDeclarations.TryGetValue(
                qualifiedName,
                out var declaration)
                ? declaration.Semantic.CoreSymbol
                : null;
            member.Semantic.MemberReference =
                new CoreMemberReferenceInfo.ModuleSymbol(
                    symbol ?? new CoreSymbolInfo(member.MemberName));
            return;
        }

        if (member.Target.Semantic.Type is not { } targetType
            || TypeRefFacts.GetBaseName(
                TypeRefFacts.StripPointersAndAliases(targetType)) is not
                { } targetTypeName)
        {
            return;
        }

        if (enums.TryGetValue(targetTypeName, out var dataEnum)
            && dataEnum.IsDataEnum
            && dataEnum.DataFields?.FirstOrDefault(field =>
                string.Equals(
                    field.Name,
                    member.MemberName,
                    StringComparison.Ordinal)) is { } dataField)
        {
            member.Semantic.MemberReference =
                new CoreMemberReferenceInfo.DataEnumField(
                    dataEnum,
                    dataField);
            return;
        }

        if (taggedUnions.TryGetValue(
                targetTypeName,
                out var taggedUnion)
            && taggedUnion.Variants.FirstOrDefault(variant =>
                string.Equals(
                    variant.Name,
                    member.MemberName,
                    StringComparison.Ordinal)) is { } variant)
        {
            member.Semantic.MemberReference =
                new CoreMemberReferenceInfo.TaggedUnionVariant(
                    taggedUnion,
                    variant);
            return;
        }

        var storageType = TypeAdapterStorageResolver.Resolve(
            TypeRefFacts.StripPointersAndAliases(targetType),
            typeAdapters);
        if (TypeRefFacts.GetBaseName(storageType) is not
                { } storageTypeName
            || !structs.TryGetValue(
                storageTypeName,
                out var structNode)
            || structNode.Fields.FirstOrDefault(field =>
                string.Equals(
                    field.Name,
                    member.MemberName,
                    StringComparison.Ordinal)) is not { } structField)
        {
            return;
        }

        var fieldType = structField.TypeNode?.Semantic.Type
            ?? structField.TypeNode.ToTypeRef(typeRefParser);
        if (fieldType is not TypeRef.Unknown)
        {
            member.Semantic.MemberReference =
                new CoreMemberReferenceInfo.StructField(
                    structNode,
                    structField,
                    fieldType);
        }
    }

    private static CoreMemberReferenceInfo.InterfaceTypeId?
        TryGetInterfaceTypeIdReference(
            MemberExpressionNode member,
            IReadOnlyDictionary<string, InterfaceNode> interfaces)
    {
        if (member is not
            {
                MemberName: "type_id",
                Target: MemberExpressionNode
                {
                    MemberName: "vtable",
                } vtable,
            }
            || vtable.Target.Semantic.Type is not { } targetType
            || TypeRefFacts.GetBaseName(
                TypeRefFacts.StripPointersAndAliases(targetType)) is not
                { } interfaceName
            || !interfaces.TryGetValue(
                interfaceName,
                out var interfaceNode))
        {
            return null;
        }

        return new CoreMemberReferenceInfo.InterfaceTypeId(interfaceNode);
    }

    private static void AnnotateName(
        NameExpressionNode name,
        IReadOnlyDictionary<string, TopLevelNode> linkedDeclarations,
        IReadOnlyDictionary<string, string> symbolAliases)
    {
        if (name.Semantic.Symbol?.Node is FunctionNode function
            && function.TypeParameters.Count == 0)
        {
            name.Semantic.CoreFunctionReference =
                new CoreFunctionReferenceInfo(function);
        }

        var symbol = name.Semantic.Symbol?.Node?.Semantic.CoreSymbol;
        if (symbol is null
            && linkedDeclarations.TryGetValue(
                name.Name,
                out var declaration))
        {
            symbol = declaration.Semantic.CoreSymbol;
        }

        if (symbol is null
            && symbolAliases.TryGetValue(
                name.Name,
                out var original))
        {
            symbol = new CoreSymbolInfo(original);
        }

        name.Semantic.CoreSymbol = symbol;
    }

    private static string DeclarationName(TopLevelNode declaration) =>
        declaration switch
        {
            GlobalVariableNode global => global.Name,
            ExternFunctionNode function => function.Name,
            _ => string.Empty,
        };

    private static string UnqualifiedName(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0
            ? name
            : name[(separator + 1)..];
    }
}
