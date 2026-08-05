using Cx.Compiler.Diagnostics;
using Cx.Compiler.Modules;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeFunctionSymbol(
    CompileTimeSymbolId Id,
    FunctionNode Declaration,
    string Name,
    string DeclaringModule,
    DeclarationVisibility Visibility) : ICompileTimeSymbol
{
    public bool IsPublic => Visibility == DeclarationVisibility.Public;
}

internal sealed class CompileTimeFunctionRegistry
{
    private readonly IReadOnlyList<CompileTimeFunctionSymbol> _functions;
    private readonly CompileTimeModuleContext _modules;
    private readonly CompileTimeSymbolResolver<CompileTimeFunctionSymbol> _resolver;
    private readonly CompileTimeScriptTypeRegistry _types;
    private readonly ModuleOwnership? _moduleOwnership;

    private CompileTimeFunctionRegistry(
        IReadOnlyList<CompileTimeFunctionSymbol> functions,
        CompileTimeModuleContext modules,
        CompileTimeScriptTypeRegistry types,
        ModuleOwnership? moduleOwnership)
    {
        _functions = functions;
        _modules = modules;
        _resolver = new CompileTimeSymbolResolver<CompileTimeFunctionSymbol>(
            functions,
            modules);
        _types = types;
        _moduleOwnership = moduleOwnership;
    }

    public static CompileTimeFunctionRegistry Empty { get; } =
        new(
            [],
            CompileTimeModuleContext.Empty,
            CompileTimeScriptTypeRegistry.Default,
            moduleOwnership: null);

    public static CompileTimeFunctionRegistry Create(
        ProgramNode program,
        CompileTimeScriptTypeRegistry? types = null,
        CompileTimeModuleContext? modules = null)
    {
        var moduleContext = modules ?? CompileTimeModuleContext.Create([program]);
        var functions = program.Functions
            .Where(function => function.IsCompileTime)
            .Select(function => CreateSymbol(function, moduleContext))
            .GroupBy(symbol => symbol.Id)
            .Select(group => group.First())
            .ToList();
        return new CompileTimeFunctionRegistry(
            functions,
            moduleContext,
            types ?? CompileTimeScriptTypeRegistry.Default,
            ModuleOwnership.Create(program));
    }

    public CompileTimeScriptTypeRegistry Types => _types;

    public CompileTimeModuleContext Modules => _modules;

    public string ModuleFor(FunctionNode function) =>
        _modules.ModuleFor(function);

    public string ModuleFor(SyntaxNode syntax) =>
        _moduleOwnership is not null
        && _moduleOwnership.TryGetOwnedModuleName(
            syntax,
            out var moduleName)
            ? moduleName
            : _modules.ModuleForPath(
                syntax.Location.File.Path);

    public CompileTimeSymbolLookup<CompileTimeFunctionSymbol> Lookup(
        string requestedName,
        string callerModule) =>
        _resolver.Lookup(requestedName, callerModule);

    private static CompileTimeFunctionSymbol CreateSymbol(
        FunctionNode function,
        CompileTimeModuleContext modules)
    {
        var original = modules.TryGetOriginal<FunctionNode>(
            function,
            out var originalFunction)
            ? originalFunction
            : function;
        var declaringModule = modules.ModuleFor(function);
        return new CompileTimeFunctionSymbol(
            new CompileTimeSymbolId(
                declaringModule,
                original.Name,
                function.Location.File.Path,
                function.Location.Position),
            function,
            original.Name,
            declaringModule,
            original.Visibility);
    }

    public void Validate(DiagnosticBag diagnostics)
    {
        foreach (var function in _functions.Select(symbol => symbol.Declaration))
        {
            foreach (var parameter in function.Parameters)
            {
                if (!_types.IsSupported(parameter.TypeNode))
                {
                    diagnostics.Report(
                        parameter.Location,
                        $"Compile-time function parameter '{parameter.Name}' uses unsupported type '{CompileTimeScriptTypeRegistry.Display(parameter.TypeNode)}'.");
                }
            }

            if (!_types.IsSupported(function.ReturnTypeNode))
            {
                diagnostics.Report(
                    function.Location,
                    $"Compile-time function '{function.Name}' uses unsupported return type '{CompileTimeScriptTypeRegistry.Display(function.ReturnTypeNode)}'.");
            }

            ValidateStatements(function.Body, diagnostics, loopDepth: 0);
            if (!ContainsReturn(function.Body))
            {
                diagnostics.Report(
                    function.Location,
                    $"Compile-time function '{function.Name}' must contain a value-returning statement.");
            }
        }
    }

    private void ValidateStatements(
        IReadOnlyList<StatementNode> statements,
        DiagnosticBag diagnostics,
        int loopDepth)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatement { Expression: null }:
                    diagnostics.Report(
                        statement.Location,
                        "Compile-time functions must return a value.");
                    break;
                case ReturnStatement:
                case CStatement:
                    break;
                case BreakStatement:
                case ContinueStatement:
                    if (loopDepth == 0)
                    {
                        diagnostics.Report(
                            statement.Location,
                            "'break' and 'continue' are only valid inside a compile-time foreach loop.");
                    }
                    break;
                case LetStatement binding:
                    if (binding.Initializer is null)
                    {
                        diagnostics.Report(
                            binding.Location,
                            $"Compile-time binding '{binding.Name}' requires an initializer.");
                    }
                    if (binding.TypeNode is not null
                        && !_types.IsSupported(binding.TypeNode))
                    {
                        diagnostics.Report(
                            binding.Location,
                            $"Compile-time binding '{binding.Name}' uses unsupported type '{CompileTimeScriptTypeRegistry.Display(binding.TypeNode)}'.");
                    }
                    break;
                case IfStatement conditional:
                    ValidateStatements(conditional.ThenBody, diagnostics, loopDepth);
                    ValidateElseBranch(conditional.ElseBranch, diagnostics, loopDepth);
                    break;
                case ForeachStatement loop:
                    ValidateForeach(loop, diagnostics, loopDepth);
                    break;
                default:
                    diagnostics.Report(
                        statement.Location,
                        $"Statement '{statement.GetType().Name}' is not supported inside compile-time functions.");
                    break;
            }
        }
    }

    private void ValidateElseBranch(
        StatementNode? elseBranch,
        DiagnosticBag diagnostics,
        int loopDepth)
    {
        switch (elseBranch)
        {
            case null:
                break;
            case ElseBlockStatement elseBlock:
                ValidateStatements(elseBlock.Body, diagnostics, loopDepth);
                break;
            case IfStatement elseIf:
                ValidateStatements([elseIf], diagnostics, loopDepth);
                break;
            default:
                diagnostics.Report(
                    elseBranch.Location,
                    $"Statement '{elseBranch.GetType().Name}' is not supported inside compile-time functions.");
                break;
        }
    }

    private void ValidateForeach(
        ForeachStatement loop,
        DiagnosticBag diagnostics,
        int loopDepth)
    {
        if (loop.KeyBinding is not null)
        {
            diagnostics.Report(
                loop.KeyBinding.Location,
                "Compile-time foreach over lists does not support a key binding.");
        }

        foreach (var binding in new[] { loop.IndexBinding, loop.ValueBinding })
        {
            if (binding is null)
            {
                continue;
            }

            if (binding.IsReference)
            {
                diagnostics.Report(
                    binding.Location,
                    "Compile-time foreach bindings cannot use '&'.");
            }

            if (binding.TypeNode is not null
                && !_types.IsSupported(binding.TypeNode))
            {
                diagnostics.Report(
                    binding.Location,
                    $"Compile-time foreach binding '{binding.Name}' uses unsupported type '{CompileTimeScriptTypeRegistry.Display(binding.TypeNode)}'.");
            }
        }

        ValidateStatements(loop.Body, diagnostics, loopDepth + 1);
    }

    private static bool ContainsReturn(IReadOnlyList<StatementNode> statements) =>
        statements.Any(statement => statement switch
        {
            ReturnStatement { Expression: not null } => true,
            IfStatement conditional =>
                ContainsReturn(conditional.ThenBody)
                || ContainsReturnInElse(conditional.ElseBranch),
            ForeachStatement loop => ContainsReturn(loop.Body),
            _ => false,
        });

    private static bool ContainsReturnInElse(StatementNode? elseBranch) =>
        elseBranch switch
        {
            ElseBlockStatement elseBlock => ContainsReturn(elseBlock.Body),
            IfStatement elseIf => ContainsReturn([elseIf]),
            _ => false,
        };
}

internal sealed class CompileTimeModuleContext
{
    private readonly IReadOnlyDictionary<string, string> _moduleNamesByPath;
    private readonly IReadOnlyDictionary<SourceIdentity, string>
        _modulesByDeclaration;
    private readonly IReadOnlyDictionary<SourceIdentity, TopLevelNode>
        _originalDeclarations;
    private readonly IReadOnlyDictionary<string, ModuleImports> _importsByModule;
    private readonly IReadOnlySet<string> _moduleNames;

    private CompileTimeModuleContext(
        IReadOnlyDictionary<string, string> moduleNamesByPath,
        IReadOnlyDictionary<SourceIdentity, string> modulesByDeclaration,
        IReadOnlyDictionary<SourceIdentity, TopLevelNode> originalDeclarations,
        IReadOnlyDictionary<string, ModuleImports> importsByModule,
        IReadOnlySet<string> moduleNames)
    {
        _moduleNamesByPath = moduleNamesByPath;
        _modulesByDeclaration = modulesByDeclaration;
        _originalDeclarations = originalDeclarations;
        _importsByModule = importsByModule;
        _moduleNames = moduleNames;
    }

    public static CompileTimeModuleContext Empty { get; } = Create([]);

    public static CompileTimeModuleContext Create(
        IReadOnlyList<ProgramNode> programs,
        IReadOnlyDictionary<string, string>? moduleNamesByPath = null)
    {
        var paths = moduleNamesByPath
            ?? ModuleProgramFacts
                .BuildUnambiguousModuleNamesByPath(
                    ModuleUnit.FromPrograms(programs));
        var originalDeclarations = programs
            .SelectMany(program => program.Declarations.Select(declaration => new
            {
                Key = new SourceIdentity(
                    declaration.Location.File.Path,
                    declaration.Location.Position),
                Value = declaration,
            }))
            .GroupBy(item => item.Key)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value);
        var modulesByDeclaration = programs
            .SelectMany(program =>
            {
                var ownership = ModuleOwnership.Create(
                    program,
                    paths);
                return program.Declarations.Select(
                    declaration => new
                    {
                        Key = new SourceIdentity(
                            declaration.Location.File.Path,
                            declaration.Location.Position),
                        Module = ownership
                            .GetDeclarationModuleName(
                                declaration),
                    });
            })
            .GroupBy(item => item.Key)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Module)
                    .Distinct(StringComparer.Ordinal)
                    .Single());
        var ownedImports = programs
            .SelectMany(program =>
            {
                var ownership = ModuleOwnership.Create(
                    program,
                    paths);
                return program.Declarations
                    .Where(declaration => declaration
                        is ImportNode
                        or SymbolImportNode)
                    .Select(declaration => new
                    {
                        Module = ownership
                            .GetDeclarationModuleName(
                                declaration),
                        Declaration = declaration,
                    });
            })
            .ToList();
        var imports = ownedImports
            .GroupBy(
                item => item.Module,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ModuleImports.Create(
                    group.Select(item =>
                        item.Declaration)),
                StringComparer.Ordinal);
        var modules = programs
            .Select(ModuleProgramFacts.GetModuleName)
            .Concat(modulesByDeclaration.Values)
            .Append("std.core")
            .ToHashSet(StringComparer.Ordinal);
        return new CompileTimeModuleContext(
            paths,
            modulesByDeclaration,
            originalDeclarations,
            imports,
            modules);
    }

    public bool TryGetOriginal<TNode>(
        TNode projected,
        out TNode original)
        where TNode : TopLevelNode
    {
        var source = new SourceIdentity(
            projected.Location.File.Path,
            projected.Location.Position);
        if (_originalDeclarations.TryGetValue(source, out var declaration)
            && declaration is TNode typed)
        {
            original = typed;
            return true;
        }

        original = null!;
        return false;
    }

    public string ModuleFor(TopLevelNode declaration)
    {
        if (!string.IsNullOrWhiteSpace(
            declaration.Semantic.ModuleName))
        {
            return declaration.Semantic.ModuleName;
        }

        if (declaration.GeneratedFrom is { } generated)
        {
            return ModuleForPath(
                generated.InvocationSpan.Location.File.Path);
        }

        var source = new SourceIdentity(
            declaration.Location.File.Path,
            declaration.Location.Position);
        return _modulesByDeclaration.GetValueOrDefault(
            source,
            ModuleForPath(declaration.Location.File.Path));
    }

    public string ModuleForPath(string path) =>
        _moduleNamesByPath.GetValueOrDefault(path, string.Empty);

    public CompileTimeSymbolReference ResolveReference(
        string requestedName,
        string callerModule)
    {
        if (!requestedName.Contains('.', StringComparison.Ordinal))
        {
            return new CompileTimeSymbolReference.Unqualified(requestedName);
        }

        var qualifiers = Aliases(callerModule)
            .Select(alias => (Qualifier: alias.Key, Module: alias.Value))
            .Concat(BareImports(callerModule).Select(module => (
                Qualifier: module,
                Module: module)))
            .Append((Qualifier: callerModule, Module: callerModule))
            .Where(item => item.Qualifier.Length > 0)
            .Where(item => requestedName.StartsWith(
                item.Qualifier + ".",
                StringComparison.Ordinal))
            .OrderByDescending(item => item.Qualifier.Length)
            .ToList();
        if (qualifiers.FirstOrDefault() is { Qualifier: { } qualifier } match)
        {
            return new CompileTimeSymbolReference.Qualified(
                match.Module,
                requestedName[(qualifier.Length + 1)..]);
        }

        var unimportedModule = _moduleNames
            .Where(module => module.Length > 0)
            .Where(module => requestedName.StartsWith(
                module + ".",
                StringComparison.Ordinal))
            .OrderByDescending(module => module.Length)
            .FirstOrDefault();
        if (unimportedModule is not null)
        {
            return new CompileTimeSymbolReference.Unimported(
                unimportedModule,
                requestedName[(unimportedModule.Length + 1)..]);
        }

        return new CompileTimeSymbolReference.UnrecognizedQualifier();
    }

    public IReadOnlyList<string> BareImports(string callerModule) =>
        Imports(callerModule).BareModules
            .Append("std.core")
            .Where(module => !string.Equals(module, callerModule, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<string> ImportedModules(string callerModule) =>
        Imports(callerModule).BareModules
            .Concat(Imports(callerModule).Aliases.Values)
            .Concat(Imports(callerModule).Symbols.Values.Select(symbol => symbol.ModuleName))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public bool IsImported(string callerModule, string moduleName) =>
        string.Equals(callerModule, moduleName, StringComparison.Ordinal)
        || BareImports(callerModule).Contains(moduleName, StringComparer.Ordinal)
        || Aliases(callerModule).Values.Contains(moduleName, StringComparer.Ordinal)
        || Imports(callerModule).Symbols.Values.Any(symbol =>
            string.Equals(symbol.ModuleName, moduleName, StringComparison.Ordinal));

    public bool TryResolveSymbolImport(
        string callerModule,
        string visibleName,
        out string moduleName,
        out string sourceName)
    {
        if (Imports(callerModule).Symbols.TryGetValue(visibleName, out var symbol))
        {
            moduleName = symbol.ModuleName;
            sourceName = symbol.SourceName;
            return true;
        }

        moduleName = string.Empty;
        sourceName = visibleName;
        return false;
    }

    private IReadOnlyDictionary<string, string> Aliases(string callerModule) =>
        Imports(callerModule).Aliases;

    private ModuleImports Imports(string callerModule) =>
        _importsByModule.GetValueOrDefault(callerModule, ModuleImports.Empty);

    private readonly record struct SourceIdentity(string Path, int Position);

    private sealed record ModuleImports(
        IReadOnlySet<string> BareModules,
        IReadOnlyDictionary<string, string> Aliases,
        IReadOnlyDictionary<string, ImportedCompileTimeSymbol> Symbols)
    {
        public static ModuleImports Empty { get; } = new(
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, ImportedCompileTimeSymbol>(StringComparer.Ordinal));

        public static ModuleImports Create(
            IEnumerable<TopLevelNode> declarations)
        {
            var declarationList =
                declarations.ToList();
            var imports = declarationList
                .OfType<ImportNode>()
                .ToList();
            return new ModuleImports(
                imports
                    .Where(import => import.Alias is null)
                    .Select(import => import.ModuleName)
                    .ToHashSet(StringComparer.Ordinal),
                imports
                    .Where(import => import.Alias is not null)
                    .GroupBy(import => import.Alias!, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().ModuleName,
                        StringComparer.Ordinal),
                declarationList
                    .OfType<SymbolImportNode>()
                    .SelectMany(import => import.Symbols.Select(symbol => new
                    {
                        VisibleName = symbol.Alias ?? symbol.Name,
                        Value = new ImportedCompileTimeSymbol(
                            import.ModuleName,
                            symbol.Name),
                    }))
                    .GroupBy(item => item.VisibleName, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last().Value,
                        StringComparer.Ordinal));
        }
    }

    private sealed record ImportedCompileTimeSymbol(
        string ModuleName,
        string SourceName);
}
