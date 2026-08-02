using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ImportedTypeRewriter
{
    public static string QualifyName(string alias, string name) =>
        name.Contains('.', StringComparison.Ordinal)
            ? name
            : alias + "." + name;

    public static TypeNode Qualify(
        TypeNode? typeNode,
        string alias,
        IReadOnlySet<string> declaredTypeNames) =>
        Rewrite(
            typeNode,
            name => declaredTypeNames.Contains(name)
                ? QualifyName(alias, name)
                : name);

    public static TypeNode Project(
        TypeNode? typeNode,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlySet<string> declaredTypeNames) =>
        Rewrite(
            typeNode,
            name => declaredTypeNames.Contains(name)
                && symbols.TryGetValue(name, out var visibleName)
                    ? visibleName
                    : name);

    public static TypeNode Rename(TypeNode? typeNode, string name)
    {
        var rewritten = TypeNode.Named(
            typeNode?.Location ?? Location.Synthetic("<type-rename>"),
            name);
        rewritten.Semantic.Type = rewritten.Syntax.ToUnresolvedTypeRef();
        return rewritten;
    }

    public static string GetText(TypeNode? typeNode) =>
        typeNode?.ToSourceText() ?? string.Empty;

    public static IReadOnlySet<string> GetDeclaredTypeNames(
        ProgramNode program) =>
        program.TypeAliases.Select(typeAlias => typeAlias.Name)
            .Concat(program.Structs.Select(structNode => structNode.Name))
            .Concat(program.TypeAdapters.Select(adapter => adapter.Name))
            .Concat(program.Enums.Select(enumNode => enumNode.Name))
            .Concat(program.Interfaces.Select(interfaceNode => interfaceNode.Name))
            .Concat(program.TaggedUnions.Select(union => union.Name))
            .Concat(program.CDeclarations.SelectMany(declaration =>
                declaration.TypeAliases.Select(typeAlias => typeAlias.Name)))
            .Concat(program.CDeclarations.SelectMany(declaration =>
                declaration.Structs.Select(structNode => structNode.Name)))
            .Concat(program.CDeclarations.SelectMany(declaration =>
                declaration.Enums.Select(enumNode => enumNode.Name)))
            .Concat(program.CDeclarations.SelectMany(declaration =>
                declaration.Unions.Select(union => union.Name)))
            .ToHashSet(StringComparer.Ordinal);

    private static TypeNode Rewrite(
        TypeNode? typeNode,
        Func<string, string> rewriteName)
    {
        var location = typeNode?.Location
            ?? Location.Synthetic("<type-rewrite>");
        var syntax = typeNode?.Syntax
            ?? new NamedTypeSyntaxNode(string.Empty);
        var rewritten = TypeNode.Create(
            location,
            Rewrite(syntax, rewriteName));
        rewritten.Semantic.Type = rewritten.Syntax.ToUnresolvedTypeRef();
        return rewritten;
    }

    private static TypeSyntaxNode Rewrite(
        TypeSyntaxNode syntax,
        Func<string, string> rewriteName) =>
        syntax switch
        {
            NamedTypeSyntaxNode named =>
                named with { Name = rewriteName(named.Name) },
            GenericTypeSyntaxNode generic => generic with
            {
                Target = Rewrite(generic.Target, rewriteName),
                Arguments = generic.Arguments
                    .Select(argument => Rewrite(argument, rewriteName))
                    .ToList(),
            },
            PointerTypeSyntaxNode pointer =>
                pointer with { Element = Rewrite(pointer.Element, rewriteName) },
            ConstTypeSyntaxNode constType =>
                constType with { Element = Rewrite(constType.Element, rewriteName) },
            NullableTypeSyntaxNode nullable =>
                nullable with { Element = Rewrite(nullable.Element, rewriteName) },
            FixedArrayTypeSyntaxNode array =>
                array with { Element = Rewrite(array.Element, rewriteName) },
            FunctionTypeSyntaxNode function => function with
            {
                Parameters = function.Parameters
                    .Select(parameter => Rewrite(parameter, rewriteName))
                    .ToList(),
                ReturnType = Rewrite(function.ReturnType, rewriteName),
            },
            _ => syntax,
        };
}
