using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal abstract record TypeRef
{
    public static Named Void { get; } = new("void", []);

    public static Named Bool { get; } = new("bool", []);

    public static Named Int { get; } = new("int", []);

    public static Named Char { get; } = new("char", []);

    public static Named Double { get; } = new("double", []);

    public static Named Usize { get; } = new("usize", []);

    public static Named U8 { get; } = new("u8", []);

    public static Named Any { get; } = new("any", []);

    public sealed record Unknown : TypeRef;

    public sealed record Null : TypeRef;

    public sealed record Named(
        string Name,
        IReadOnlyList<TypeRef> Arguments,
        string? ModuleName = null) : TypeRef;

    public sealed record Alias(string Name, TypeRef Target) : TypeRef;

    public sealed record Pointer(TypeRef Element) : TypeRef;

    public sealed record Const(TypeRef Element) : TypeRef;

    public sealed record FixedArray(TypeRef Element, ArrayLengthNode Length) : TypeRef;

    public sealed record Function(IReadOnlyList<TypeRef> Parameters, TypeRef ReturnType, bool IsVariadic = false) : TypeRef;
}

internal sealed class TypeRefParser(
    ProgramNode program,
    ProgramDeclarationIndex? declarationIndex = null,
    string? currentModuleName = null)
{
    private readonly ProgramDeclarationIndex _declarations =
        declarationIndex ?? ProgramDeclarationIndex.Create(program);
    private readonly string _currentModuleName =
        currentModuleName ?? program.Module?.Name ?? string.Empty;

    public bool IsEnumName(string name) =>
        IsEnum(new TypeRef.Named(name, []));

    public bool IsEnum(
        TypeRef type,
        string? currentModuleName = null)
    {
        var unwrapped = TypeRefFacts.UnwrapConst(
            TypeRefFacts.UnwrapAlias(type));
        if (unwrapped is not TypeRef.Named named)
        {
            return false;
        }

        var lookup = _declarations.LookupTypeFromModule(
            currentModuleName ?? _currentModuleName,
            named);
        if (lookup
            is ProgramTypeDeclarationLookup.Found
            {
                Declaration: EnumNode,
            })
        {
            return true;
        }

        return lookup is ProgramTypeDeclarationLookup.Missing
            && program.CDeclarations
                .SelectMany(declaration => declaration.Enums)
                .Any(enumNode => string.Equals(
                    enumNode.Name,
                    named.Name,
                    StringComparison.Ordinal));
    }

    public TypeRef Parse(TypeNode? typeNode)
    {
        if (typeNode is null)
        {
            return new TypeRef.Unknown();
        }

        if (typeNode.Semantic.Type is { } semanticType)
        {
            return semanticType;
        }

        if (string.IsNullOrWhiteSpace(typeNode.ToSourceText()))
        {
            return new TypeRef.Unknown();
        }

        return Parse(typeNode.Syntax, [], _currentModuleName);
    }

    public TypeRef Parse(string? type)
    {
        var syntax = TypeSyntaxParser.Parse(type);
        return ParseSyntax(syntax);
    }

    public TypeRef ParseSyntax(
        TypeSyntaxNode? syntax,
        string? currentModuleName = null) =>
        syntax is null
            ? new TypeRef.Unknown()
            : Parse(
                syntax,
                [],
                currentModuleName ?? _currentModuleName);

    private TypeRef Parse(
        TypeSyntaxNode syntax,
        HashSet<(string ModuleName, string Name)> resolvingAliases,
        string currentModuleName) =>
        syntax switch
        {
            NamedTypeSyntaxNode named => ParseNamedSyntax(named, resolvingAliases, currentModuleName),
            GenericTypeSyntaxNode generic => ParseGenericSyntax(generic, resolvingAliases, currentModuleName),
            PointerTypeSyntaxNode pointer => new TypeRef.Pointer(Parse(pointer.Element, resolvingAliases, currentModuleName)),
            ConstTypeSyntaxNode constType => new TypeRef.Const(Parse(constType.Element, resolvingAliases, currentModuleName)),
            FixedArrayTypeSyntaxNode array => new TypeRef.FixedArray(Parse(array.Element, resolvingAliases, currentModuleName), array.Length),
            FunctionTypeSyntaxNode function => new TypeRef.Function(
                function.Parameters
                    .Select(parameter => Parse(
                        parameter,
                        new HashSet<(string ModuleName, string Name)>(resolvingAliases),
                        currentModuleName))
                    .ToList(),
                Parse(function.ReturnType, resolvingAliases, currentModuleName),
                function.IsVariadic),
            _ => new TypeRef.Unknown(),
        };

    private TypeRef ParseNamedSyntax(
        NamedTypeSyntaxNode named,
        HashSet<(string ModuleName, string Name)> resolvingAliases,
        string currentModuleName)
    {
        if (string.IsNullOrWhiteSpace(named.Name))
        {
            return new TypeRef.Unknown();
        }

        if (string.Equals(named.Name, "null", StringComparison.Ordinal))
        {
            return new TypeRef.Null();
        }

        var lookup = _declarations.LookupTypeFromModule(
            currentModuleName,
            new TypeRef.Named(named.Name, []));
        if (!TrySelectAlias(
                lookup,
                out var alias,
                out var declaredModuleName))
        {
            return new TypeRef.Named(named.Name, []);
        }

        var aliasModuleName =
            declaredModuleName
            ?? alias.Semantic.ModuleName
            ?? currentModuleName;
        var aliasIdentity = (aliasModuleName, named.Name);
        if (!resolvingAliases.Add(aliasIdentity))
        {
            return new TypeRef.Named(named.Name, []);
        }

        var target = ParseAliasTarget(
            alias.TargetTypeNode,
            resolvingAliases,
            aliasModuleName);
        resolvingAliases.Remove(aliasIdentity);
        return new TypeRef.Alias(named.Name, target);
    }

    private static bool TrySelectAlias(
        ProgramTypeDeclarationLookup lookup,
        out TypeAliasNode alias,
        out string? moduleName)
    {
        if (lookup
            is ProgramTypeDeclarationLookup.Found
            {
                Declaration: TypeAliasNode foundAlias,
                ModuleName: var foundModuleName,
            })
        {
            alias = foundAlias;
            moduleName = foundModuleName;
            return true;
        }

        if (lookup
            is ProgramTypeDeclarationLookup.Ambiguous
            {
                Declarations: var declarations,
            }
            && declarations.All(
                declaration =>
                    declaration is TypeAliasNode
                    {
                        IsHeaderDeclaration: true,
                        TargetTypeNode: not null,
                    })
            && declarations
                .Cast<TypeAliasNode>()
                .Select(declaration =>
                    declaration.TargetTypeNode!.Syntax)
                .Distinct()
                .Count() == 1)
        {
            alias = (TypeAliasNode)declarations[0];
            moduleName = alias.Semantic.ModuleName;
            return true;
        }

        alias = null!;
        moduleName = null;
        return false;
    }

    private TypeRef ParseAliasTarget(
        TypeNode? targetType,
        HashSet<(string ModuleName, string Name)> resolvingAliases,
        string currentModuleName)
    {
        if (targetType is null || string.IsNullOrWhiteSpace(targetType.ToSourceText()))
        {
            return new TypeRef.Unknown();
        }

        if (targetType.Semantic.Type is { } semanticType)
        {
            return semanticType;
        }

        return Parse(
            targetType.Syntax,
            resolvingAliases,
            currentModuleName);
    }

    private TypeRef ParseGenericSyntax(
        GenericTypeSyntaxNode generic,
        HashSet<(string ModuleName, string Name)> resolvingAliases,
        string currentModuleName)
    {
        var name = TypeSyntaxFormatter.ToCxString(generic.Target);
        return new TypeRef.Named(
            name,
            generic.Arguments
                .Select(argument => Parse(
                    argument,
                    new HashSet<(string ModuleName, string Name)>(
                        resolvingAliases),
                    currentModuleName))
                .ToList());
    }

}

internal sealed class TypeCompatibility(TypeRefParser parser)
{
    public bool CanAssign(TypeRef targetType, TypeRef? sourceType, out string reason)
    {
        reason = string.Empty;
        if (sourceType is null)
        {
            return true;
        }

        var target = targetType;
        var source = sourceType;
        if (IsUnknown(target) || IsUnknown(source))
        {
            return true;
        }

        if (IsAssignable(target, source))
        {
            return true;
        }

        reason = $"cannot assign '{TypeRefFormatter.ToCxString(source)}' to '{TypeRefFormatter.ToCxString(target)}'";
        return false;
    }

    private bool IsAssignable(TypeRef target, TypeRef source)
    {
        target = UnwrapAlias(target);
        source = UnwrapAlias(source);

        if (target is TypeRef.Unknown || source is TypeRef.Unknown)
        {
            return true;
        }

        if (target is TypeRef.Named { Name: "any" } || source is TypeRef.Named { Name: "any" })
        {
            return true;
        }

        if (source is TypeRef.Null)
        {
            return target is TypeRef.Pointer or TypeRef.Function;
        }

        if (target is TypeRef.Pointer targetPointer && source is TypeRef.Pointer sourcePointer)
        {
            return IsAssignablePointer(targetPointer.Element, sourcePointer.Element);
        }

        if (target is TypeRef.Const targetConst)
        {
            return IsAssignable(targetConst.Element, source is TypeRef.Const sourceConst ? sourceConst.Element : source);
        }

        if (source is TypeRef.Const sourceConstValue)
        {
            return IsAssignable(target, sourceConstValue.Element);
        }

        if (target is TypeRef.Named targetNamed && source is TypeRef.Named sourceNamed)
        {
            if (IsIntegerCompatible(targetNamed)
                && IsIntegerCompatible(sourceNamed))
            {
                return true;
            }

            return string.Equals(targetNamed.Name, sourceNamed.Name, StringComparison.Ordinal)
                && targetNamed.Arguments.Count == sourceNamed.Arguments.Count
                && targetNamed.Arguments.Zip(sourceNamed.Arguments).All(pair => IsAssignable(pair.First, pair.Second));
        }

        if (target is TypeRef.FixedArray targetArray && source is TypeRef.FixedArray sourceArray)
        {
            return targetArray.Length == sourceArray.Length
                && IsAssignable(targetArray.Element, sourceArray.Element);
        }

        if (target is TypeRef.Function targetFunction && source is TypeRef.Function sourceFunction)
        {
            return targetFunction.Parameters.Count == sourceFunction.Parameters.Count
                && targetFunction.IsVariadic == sourceFunction.IsVariadic
                && targetFunction.Parameters.Zip(sourceFunction.Parameters).All(pair => IsAssignable(pair.First, pair.Second))
                && IsAssignable(targetFunction.ReturnType, sourceFunction.ReturnType);
        }

        return false;
    }

    private bool IsAssignablePointer(TypeRef target, TypeRef source)
    {
        target = UnwrapAlias(target);
        source = UnwrapAlias(source);

        if (IsVoidPointerElement(target) || IsVoidPointerElement(source))
        {
            return true;
        }

        if (target is TypeRef.Const targetConst)
        {
            return IsAssignable(
                targetConst.Element,
                source is TypeRef.Const sourceConst ? sourceConst.Element : source);
        }

        if (source is TypeRef.Const)
        {
            return false;
        }

        if (IsAssignable(target, source))
        {
            return true;
        }

        return false;
    }

    private static bool IsVoidPointerElement(TypeRef type) =>
        UnwrapConst(UnwrapAlias(type)) is TypeRef.Named { Name: "void", Arguments: { Count: 0 } };

    private static bool IsUnknown(TypeRef type) => UnwrapAlias(type) is TypeRef.Unknown;

    private static TypeRef UnwrapAlias(TypeRef type)
    {
        while (type is TypeRef.Alias alias)
        {
            type = alias.Target;
        }

        return type;
    }

    private bool IsIntegerCompatible(TypeRef.Named type) =>
        BuiltinTypes.IsNumeric(type.Name)
        || parser.IsEnum(type);

    private static TypeRef UnwrapConst(TypeRef type) =>
        type is TypeRef.Const constType ? constType.Element : type;

}
