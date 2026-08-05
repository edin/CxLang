using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal sealed class ModuleSymbolIndex
{
    private readonly IReadOnlyDictionary<string, ModuleSymbols> _modules;

    private ModuleSymbolIndex(
        IReadOnlyDictionary<string, ModuleSymbols> modules)
    {
        _modules = modules;
    }

    public static ModuleSymbolIndex From(
        IEnumerable<ModuleUnit> units) =>
        new(units
            .GroupBy(
                unit => unit.Name,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ModuleSymbols.From(
                    group.Select(unit =>
                        unit.Program)),
                StringComparer.Ordinal));

    public ModuleVisibilityContext VisibilityFor(
        string moduleName,
        IEnumerable<ModuleUnit> units) =>
        ModuleVisibilityContext.From(
            moduleName,
            units.Select(unit =>
                unit.Program),
            _modules);
}

internal sealed record ModuleVisibilityContext(
    string ModuleName,
    IReadOnlyDictionary<string, ModuleSymbols> Modules,
    IReadOnlySet<string> BareModules,
    IReadOnlyDictionary<string, string> Aliases,
    IReadOnlyDictionary<string, ImportedSymbol> SymbolImports)
{
    public static ModuleVisibilityContext From(
        string moduleName,
        IEnumerable<ProgramNode> programs,
        IReadOnlyDictionary<string, ModuleSymbols> modules)
    {
        var imports = programs.SelectMany(program => program.Imports).ToList();
        var symbolImports = programs.SelectMany(program => program.SymbolImports).ToList();
        return new ModuleVisibilityContext(
            moduleName,
            modules,
            imports.Where(import => import.Alias is null)
                .Select(import => import.ModuleName)
                .Append(moduleName)
                .Append("std.core")
                .ToHashSet(StringComparer.Ordinal),
            imports.Where(import => import.Alias is not null)
                .GroupBy(import => import.Alias!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().ModuleName, StringComparer.Ordinal),
            symbolImports.SelectMany(import => import.Symbols.Select(symbol =>
                    new ImportedSymbol(symbol.Alias ?? symbol.Name, symbol.Name, import.ModuleName)))
                .GroupBy(symbol => symbol.VisibleName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal));
    }

    public bool SymbolExistsAsType(string name) => SymbolExists(name, symbols => symbols.Types);

    public bool SymbolExistsAsValue(string name) => SymbolExists(name, symbols => symbols.Values);

    public bool SymbolExistsAsFunction(string name) => SymbolExists(name, symbols => symbols.Functions);

    public bool IsVisibleType(string name) => IsVisible(name, symbols => symbols.Types, symbols => symbols.PublicTypes);

    public bool IsVisibleValue(string name) => IsVisible(name, symbols => symbols.Values, symbols => symbols.PublicValues);

    public bool IsVisibleFunction(string name) => IsVisible(name, symbols => symbols.Functions, symbols => symbols.PublicFunctions);

    public bool IsPrivateTypeInCurrentModule(string name)
    {
        if (TryResolveQualifiedName(name, out var qualifiedModule, out var symbol))
        {
            return string.Equals(qualifiedModule, ModuleName, StringComparison.Ordinal)
                && Modules.TryGetValue(ModuleName, out var qualifiedSymbols)
                && qualifiedSymbols.Types.Contains(symbol)
                && !qualifiedSymbols.PublicTypes.Contains(symbol);
        }

        return Modules.TryGetValue(ModuleName, out var module)
            && module.Types.Contains(name)
            && !module.PublicTypes.Contains(name);
    }

    public string BuildTypeDiagnostic(string name) => BuildDiagnostic("type", name);

    public string BuildValueDiagnostic(string name) => BuildDiagnostic("symbol", name);

    public string BuildFunctionDiagnostic(string name) => BuildDiagnostic("function", name);

    private bool SymbolExists(string name, Func<ModuleSymbols, IReadOnlySet<string>> selectSymbols)
    {
        if (TryResolveQualifiedName(name, out var moduleName, out var symbol))
        {
            return Modules.TryGetValue(moduleName, out var moduleSymbols)
                && selectSymbols(moduleSymbols).Contains(symbol);
        }

        return Modules.Values.Any(module => selectSymbols(module).Contains(name));
    }

    private bool IsVisible(
        string name,
        Func<ModuleSymbols, IReadOnlySet<string>> selectSymbols,
        Func<ModuleSymbols, IReadOnlySet<string>> selectPublicSymbols)
    {
        if (TryResolveQualifiedName(name, out var moduleName, out var symbol))
        {
            return Modules.TryGetValue(moduleName, out var moduleSymbols)
                && SelectVisibleSymbols(moduleName, moduleSymbols, selectSymbols, selectPublicSymbols).Contains(symbol);
        }

        symbol = name;

        if (SymbolImports.TryGetValue(symbol, out var imported)
            && Modules.TryGetValue(imported.ModuleName, out var importedModule)
            && SelectVisibleSymbols(imported.ModuleName, importedModule, selectSymbols, selectPublicSymbols)
                .Contains(imported.SourceName))
        {
            return true;
        }

        return BareModules.Any(moduleName =>
            Modules.TryGetValue(moduleName, out var module)
            && SelectVisibleSymbols(moduleName, module, selectSymbols, selectPublicSymbols).Contains(symbol));
    }

    private IReadOnlySet<string> SelectVisibleSymbols(
        string moduleName,
        ModuleSymbols symbols,
        Func<ModuleSymbols, IReadOnlySet<string>> selectSymbols,
        Func<ModuleSymbols, IReadOnlySet<string>> selectPublicSymbols) =>
        string.Equals(moduleName, ModuleName, StringComparison.Ordinal)
            ? selectSymbols(symbols)
            : selectPublicSymbols(symbols);

    private string BuildDiagnostic(string kind, string name)
    {
        if (FindPrivateOwner(kind, name) is { } privateOwner)
        {
            return $"The {kind} '{name}' is private to module '{privateOwner}'.";
        }

        if (!TryResolveQualifiedName(name, out _, out var symbol))
        {
            symbol = name;
            foreach (var alias in Aliases)
            {
                if (Modules.TryGetValue(alias.Value, out var module)
                    && ModuleContainsPublic(module, kind, symbol))
                {
                    return $"Unknown {kind} '{name}'. Did you mean '{alias.Key}.{symbol}'?";
                }
            }

            var partiallyImportedModule = FindPartiallyImportedModuleContaining(kind, symbol);
            if (partiallyImportedModule is not null)
            {
                return $"Unknown {kind} '{name}'. Did you mean 'from {partiallyImportedModule} import {symbol}'?";
            }

            var moduleName = FindModuleContaining(kind, symbol);
            return moduleName is null
                ? $"Unknown {kind} '{name}'."
                : $"Unknown {kind} '{name}'. Did you mean to import {moduleName}?";
        }

        return $"Unknown {kind} '{name}'.";
    }

    private string? FindPrivateOwner(string kind, string name)
    {
        if (TryResolveQualifiedName(name, out var moduleName, out var symbol))
        {
            return IsPrivateExternalSymbol(moduleName, kind, symbol) ? moduleName : null;
        }

        symbol = name;

        if (SymbolImports.TryGetValue(symbol, out var imported)
            && IsPrivateExternalSymbol(imported.ModuleName, kind, imported.SourceName))
        {
            return imported.ModuleName;
        }

        return BareModules
            .Where(moduleName => !string.Equals(moduleName, ModuleName, StringComparison.Ordinal))
            .Where(moduleName => IsPrivateExternalSymbol(moduleName, kind, symbol))
            .OrderBy(moduleName => moduleName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool IsPrivateExternalSymbol(string moduleName, string kind, string symbol) =>
        !string.Equals(moduleName, ModuleName, StringComparison.Ordinal)
        && Modules.TryGetValue(moduleName, out var module)
        && ModuleContains(module, kind, symbol)
        && !ModuleContainsPublic(module, kind, symbol);

    private string? FindModuleContaining(string kind, string symbol) =>
        Modules
            .Where(item => item.Key.Length > 0)
            .Where(item => !BareModules.Contains(item.Key))
            .Where(item => ModuleContainsPublic(item.Value, kind, symbol))
            .Select(item => item.Key)
            .OrderBy(moduleName => moduleName, StringComparer.Ordinal)
            .FirstOrDefault();

    private string? FindPartiallyImportedModuleContaining(string kind, string symbol) =>
        SymbolImports.Values
            .Select(import => import.ModuleName)
            .Distinct(StringComparer.Ordinal)
            .Where(moduleName => Modules.TryGetValue(moduleName, out var module)
                && ModuleContainsPublic(module, kind, symbol))
            .OrderBy(moduleName => moduleName, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool ModuleContains(ModuleSymbols module, string kind, string symbol) => kind switch
    {
        "type" => module.Types.Contains(symbol),
        "symbol" => module.Values.Contains(symbol),
        "function" => module.Functions.Contains(symbol),
        _ => false,
    };

    private static bool ModuleContainsPublic(ModuleSymbols module, string kind, string symbol) => kind switch
    {
        "type" => module.PublicTypes.Contains(symbol),
        "symbol" => module.PublicValues.Contains(symbol),
        "function" => module.PublicFunctions.Contains(symbol),
        _ => false,
    };

    private bool TryResolveQualifiedName(string name, out string moduleName, out string symbol)
    {
        var match = Aliases
            .Select(alias => (VisibleName: alias.Key, ModuleName: alias.Value))
            .Concat(BareModules.Select(module => (VisibleName: module, ModuleName: module)))
            .Where(candidate => name.StartsWith(candidate.VisibleName + ".", StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.VisibleName.Length)
            .FirstOrDefault();

        if (match.VisibleName is not null)
        {
            moduleName = match.ModuleName;
            symbol = name[(match.VisibleName.Length + 1)..];
            return true;
        }

        moduleName = string.Empty;
        symbol = name;
        return false;
    }
}

internal sealed record ImportedSymbol(string VisibleName, string SourceName, string ModuleName);

internal sealed record ModuleSymbols(
    IReadOnlySet<string> Types,
    IReadOnlySet<string> PublicTypes,
    IReadOnlySet<string> Values,
    IReadOnlySet<string> PublicValues,
    IReadOnlySet<string> Functions,
    IReadOnlySet<string> PublicFunctions)
{
    public static ModuleSymbols From(IEnumerable<ProgramNode> programs)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var publicTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var valueNames = new HashSet<string>(StringComparer.Ordinal);
        var publicValueNames = new HashSet<string>(StringComparer.Ordinal);
        var functionNames = new HashSet<string>(StringComparer.Ordinal);
        var publicFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var program in programs)
        {
            foreach (var typeAlias in program.TypeAliases)
            {
                Add(typeNames, publicTypeNames, typeAlias.Name, typeAlias.IsPublic);
            }

            foreach (var requirement in program.Requirements)
            {
                Add(typeNames, publicTypeNames, requirement.Name, requirement.IsPublic);
            }

            foreach (var enumNode in program.Enums)
            {
                var isPublic = enumNode.IsPublic;
                Add(typeNames, publicTypeNames, enumNode.Name, isPublic);
                foreach (var member in enumNode.Members)
                {
                    Add(valueNames, publicValueNames, member.Name, isPublic);
                }
            }

            foreach (var interfaceNode in program.Interfaces)
            {
                Add(typeNames, publicTypeNames, interfaceNode.Name, interfaceNode.IsPublic);
            }

            foreach (var structNode in program.Structs)
            {
                var isPublic = structNode.IsPublic;
                Add(typeNames, publicTypeNames, structNode.Name, isPublic);
                foreach (var method in structNode.Methods.Where(method => method.IsStatic))
                {
                    Add(functionNames, publicFunctionNames, $"{structNode.Name}.{method.Name}", isPublic);
                }
            }

            foreach (var adapter in program.TypeAdapters)
            {
                Add(typeNames, publicTypeNames, adapter.Name, adapter.IsPublic);
            }

            foreach (var union in program.TaggedUnions)
            {
                var isPublic = union.IsPublic;
                Add(typeNames, publicTypeNames, union.Name, isPublic);
                foreach (var variant in union.Variants)
                {
                    Add(valueNames, publicValueNames, variant.Name, isPublic);
                    Add(functionNames, publicFunctionNames, $"{union.Name}.{variant.Name}", isPublic);
                }
            }

            foreach (var global in program.GlobalVariables)
            {
                Add(valueNames, publicValueNames, global.Name, global.IsPublic);
            }

            foreach (var constant in program.CompileTimeConstants)
            {
                Add(
                    valueNames,
                    publicValueNames,
                    constant.Name,
                    constant.IsPublic);
            }

            foreach (var function in program.Functions.Where(function => OwnerType(function) is null))
            {
                Add(functionNames, publicFunctionNames, function.Name, function.IsPublic);
            }

            foreach (var externFunction in program.ExternFunctions)
            {
                Add(functionNames, publicFunctionNames, externFunction.Name, externFunction.IsPublic);
            }

            foreach (var declaration in program.CDeclarations)
            {
                foreach (var typeAlias in declaration.TypeAliases)
                {
                    Add(typeNames, publicTypeNames, typeAlias.Name, isPublic: true);
                }

                foreach (var enumNode in declaration.Enums)
                {
                    Add(typeNames, publicTypeNames, enumNode.Name, isPublic: true);
                    foreach (var member in enumNode.Members)
                    {
                        Add(valueNames, publicValueNames, member.Name, isPublic: true);
                    }
                }

                foreach (var structNode in declaration.Structs)
                {
                    Add(typeNames, publicTypeNames, structNode.Name, isPublic: true);
                }

                foreach (var union in declaration.Unions)
                {
                    Add(typeNames, publicTypeNames, union.Name, isPublic: true);
                }

                foreach (var constant in declaration.Constants)
                {
                    Add(valueNames, publicValueNames, constant.Name, isPublic: true);
                }

                foreach (var function in declaration.Functions)
                {
                    Add(functionNames, publicFunctionNames, function.Name, isPublic: true);
                }
            }
        }

        return new ModuleSymbols(
            typeNames,
            publicTypeNames,
            valueNames,
            publicValueNames,
            functionNames,
            publicFunctionNames);
    }

    private static void Add(
        HashSet<string> symbols,
        HashSet<string> publicSymbols,
        string symbol,
        bool isPublic)
    {
        symbols.Add(symbol);
        if (isPublic)
        {
            publicSymbols.Add(symbol);
        }
    }
    private static string? OwnerType(FunctionNode function)
    {
        var type = function.OwnerTypeNode?.Semantic.Type;
        return type is null or TypeRef.Unknown
            ? null
            : TypeRefFacts.GetBaseName(type);
    }
}
