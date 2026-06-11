using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class SymbolSuggestionService(
    ProgramNode program,
    IReadOnlyList<ProgramNode>? availablePrograms,
    Func<FunctionNode, string?> ownerType)
{
    public string? FindAliasSuggestionForFunction(string name) =>
        FindAliasSuggestion(name, DefinesFunction);

    public string? FindAliasSuggestionForType(string name) =>
        FindAliasSuggestion(name, DefinesType);

    public string? FindAliasSuggestionForValue(string name) =>
        FindAliasSuggestion(name, DefinesValue);

    public string? FindPartialImportSuggestionForFunction(string name) =>
        FindPartialImportSuggestion(name, DefinesFunction);

    public string? FindPartialImportSuggestionForType(string name) =>
        FindPartialImportSuggestion(name, DefinesType);

    public string? FindPartialImportSuggestionForValue(string name) =>
        FindPartialImportSuggestion(name, DefinesValue);

    public string? FindImportSuggestionForFunction(string name) =>
        FindImportSuggestion(name, DefinesFunction);

    public string? FindImportSuggestionForType(string name) =>
        FindImportSuggestion(name, DefinesType);

    public string? FindImportSuggestionForValue(string name) =>
        FindImportSuggestion(name, DefinesValue);

    private string? FindPartialImportSuggestion(
        string name,
        Func<ProgramNode, string, bool> definesSymbol)
    {
        if (availablePrograms is null)
        {
            return null;
        }

        foreach (var import in program.SymbolImports)
        {
            if (import.Symbols.Any(symbol =>
                    string.Equals(symbol.Name, name, StringComparison.Ordinal)
                    || string.Equals(symbol.Alias, name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (availablePrograms.Any(candidate =>
                    string.Equals(candidate.Module?.Name, import.ModuleName, StringComparison.Ordinal)
                    && definesSymbol(candidate, name)))
            {
                return $"from {import.ModuleName} import {name}";
            }
        }

        return null;
    }

    private string? FindAliasSuggestion(
        string name,
        Func<ProgramNode, string, bool> definesSymbol)
    {
        if (availablePrograms is null)
        {
            return null;
        }

        foreach (var import in program.Imports.Where(import => import.Alias is not null))
        {
            if (availablePrograms.Any(candidate =>
                    string.Equals(candidate.Module?.Name, import.ModuleName, StringComparison.Ordinal)
                    && definesSymbol(candidate, name)))
            {
                return import.Alias + "." + name;
            }
        }

        return null;
    }

    private string? FindImportSuggestion(
        string name,
        Func<ProgramNode, string, bool> definesSymbol)
    {
        if (availablePrograms is null)
        {
            return null;
        }

        var visibleModules = program.Imports.Select(import => import.ModuleName)
            .Concat(program.SymbolImports.Select(import => import.ModuleName))
            .Append(program.Module?.Name ?? string.Empty)
            .Append("std.core")
            .ToHashSet(StringComparer.Ordinal);

        return availablePrograms
            .Select(candidate => new
            {
                ModuleName = candidate.Module?.Name ?? string.Empty,
                Program = candidate,
            })
            .Where(item => item.ModuleName.Length > 0)
            .Where(item => !visibleModules.Contains(item.ModuleName))
            .Where(item => definesSymbol(item.Program, name))
            .Select(item => item.ModuleName)
            .OrderBy(moduleName => moduleName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool DefinesFunction(ProgramNode candidate, string name) =>
        candidate.Functions.Any(function =>
            ownerType(function) is null
            && string.Equals(function.Name, name, StringComparison.Ordinal))
        || candidate.ExternFunctions.Any(function =>
            string.Equals(function.Name, name, StringComparison.Ordinal))
        || candidate.CDeclarations.Any(declaration =>
            declaration.Functions.Any(function =>
                string.Equals(function.Name, name, StringComparison.Ordinal)));

    private static bool DefinesType(ProgramNode candidate, string name) =>
        candidate.TypeAliases.Any(typeAlias => string.Equals(typeAlias.Name, name, StringComparison.Ordinal))
        || candidate.Structs.Any(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal))
        || candidate.Enums.Any(enumNode => string.Equals(enumNode.Name, name, StringComparison.Ordinal))
        || candidate.Interfaces.Any(interfaceNode => string.Equals(interfaceNode.Name, name, StringComparison.Ordinal))
        || candidate.TaggedUnions.Any(union => string.Equals(union.Name, name, StringComparison.Ordinal))
        || candidate.CDeclarations.Any(declaration =>
            declaration.TypeAliases.Any(typeAlias => string.Equals(typeAlias.Name, name, StringComparison.Ordinal))
            || declaration.Structs.Any(structNode => string.Equals(structNode.Name, name, StringComparison.Ordinal))
            || declaration.Enums.Any(enumNode => string.Equals(enumNode.Name, name, StringComparison.Ordinal))
            || declaration.Unions.Any(union => string.Equals(union.Name, name, StringComparison.Ordinal)));

    private static bool DefinesValue(ProgramNode candidate, string name) =>
        candidate.GlobalVariables.Any(global => string.Equals(global.Name, name, StringComparison.Ordinal))
        || candidate.Enums.Any(enumNode => enumNode.Members.Any(member => string.Equals(member.Name, name, StringComparison.Ordinal)))
        || candidate.CDeclarations.Any(declaration =>
            declaration.Constants.Any(constant => string.Equals(constant.Name, name, StringComparison.Ordinal))
            || declaration.Enums.Any(enumNode => enumNode.Members.Any(member => string.Equals(member.Name, name, StringComparison.Ordinal))));
}
