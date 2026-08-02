namespace Cx.Compiler.CompileTime;

internal readonly record struct CompileTimeSymbolId(
    string ModuleName,
    string Name,
    string SourcePath,
    int SourcePosition);

internal interface ICompileTimeSymbol
{
    string Name { get; }

    string DeclaringModule { get; }

    bool IsPublic { get; }
}

internal abstract record CompileTimeSymbolLookup<TSymbol>
    where TSymbol : ICompileTimeSymbol
{
    public sealed record Candidates(
        IReadOnlyList<TSymbol> Values) : CompileTimeSymbolLookup<TSymbol>;

    public sealed record Inaccessible(
        string RequestedName,
        string DeclaringModule) : CompileTimeSymbolLookup<TSymbol>;

    public sealed record Missing(
        string RequestedName,
        string? SuggestedModule = null) : CompileTimeSymbolLookup<TSymbol>;

    public sealed record NotSymbolReference : CompileTimeSymbolLookup<TSymbol>;
}

internal abstract record CompileTimeSymbolReference
{
    public sealed record Qualified(
        string ModuleName,
        string SourceName) : CompileTimeSymbolReference;

    public sealed record Unqualified(string Name) : CompileTimeSymbolReference;

    public sealed record Unimported(
        string ModuleName,
        string SourceName) : CompileTimeSymbolReference;

    public sealed record UnrecognizedQualifier : CompileTimeSymbolReference;
}

internal sealed class CompileTimeSymbolResolver<TSymbol>
    where TSymbol : ICompileTimeSymbol
{
    private readonly IReadOnlyList<TSymbol> _symbols;
    private readonly CompileTimeModuleContext _modules;

    public CompileTimeSymbolResolver(
        IReadOnlyList<TSymbol> symbols,
        CompileTimeModuleContext modules)
    {
        _symbols = symbols;
        _modules = modules;
    }

    public CompileTimeSymbolLookup<TSymbol> Lookup(
        string requestedName,
        string callerModule)
    {
        var reference = _modules.ResolveReference(requestedName, callerModule);
        if (reference is CompileTimeSymbolReference.UnrecognizedQualifier)
        {
            return new CompileTimeSymbolLookup<TSymbol>.NotSymbolReference();
        }
        if (reference is CompileTimeSymbolReference.Unimported unimported)
        {
            return new CompileTimeSymbolLookup<TSymbol>.Missing(
                requestedName,
                unimported.ModuleName);
        }

        var targets = reference switch
        {
            CompileTimeSymbolReference.Qualified qualified =>
                Select(qualified.ModuleName, qualified.SourceName),
            CompileTimeSymbolReference.Unqualified unqualified =>
                ResolveUnqualified(unqualified.Name, callerModule),
            _ => [],
        };
        if (targets.Count > 0)
        {
            var visible = targets
                .Where(symbol => symbol.IsPublic
                    || string.Equals(
                        symbol.DeclaringModule,
                        callerModule,
                        StringComparison.Ordinal))
                .ToList();
            if (visible.Count > 0)
            {
                return new CompileTimeSymbolLookup<TSymbol>.Candidates(visible);
            }

            return new CompileTimeSymbolLookup<TSymbol>.Inaccessible(
                requestedName,
                targets[0].DeclaringModule);
        }

        if (reference is CompileTimeSymbolReference.Qualified)
        {
            return new CompileTimeSymbolLookup<TSymbol>.Missing(requestedName);
        }

        var privateImportedOwner = _modules.ImportedModules(callerModule)
            .Where(module => Select(module, requestedName).Any())
            .OrderBy(module => module, StringComparer.Ordinal)
            .FirstOrDefault();
        if (privateImportedOwner is not null)
        {
            return new CompileTimeSymbolLookup<TSymbol>.Inaccessible(
                requestedName,
                privateImportedOwner);
        }

        var suggestedModule = _symbols
            .Where(symbol => symbol.Name == requestedName && symbol.IsPublic)
            .Select(symbol => symbol.DeclaringModule)
            .Where(module => !_modules.IsImported(callerModule, module))
            .OrderBy(module => module, StringComparer.Ordinal)
            .FirstOrDefault();
        return new CompileTimeSymbolLookup<TSymbol>.Missing(
            requestedName,
            suggestedModule);
    }

    private IReadOnlyList<TSymbol> ResolveUnqualified(
        string name,
        string callerModule)
    {
        var local = Select(callerModule, name);
        if (local.Count > 0)
        {
            return local;
        }

        if (_modules.TryResolveSymbolImport(
            callerModule,
            name,
            out var importedModule,
            out var sourceName))
        {
            return Select(importedModule, sourceName);
        }

        return _modules.BareImports(callerModule)
            .SelectMany(module => Select(module, name))
            .ToList();
    }

    private IReadOnlyList<TSymbol> Select(
        string moduleName,
        string name) =>
        _symbols
            .Where(symbol =>
                string.Equals(
                    symbol.DeclaringModule,
                    moduleName,
                    StringComparison.Ordinal)
                && string.Equals(symbol.Name, name, StringComparison.Ordinal))
            .ToList();
}
