using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeConstantSymbol(
    CompileTimeSymbolId Id,
    CompileTimeConstantNode Declaration,
    string Name,
    string DeclaringModule,
    DeclarationVisibility Visibility) : ICompileTimeSymbol
{
    public bool IsPublic => Visibility == DeclarationVisibility.Public;
}

internal sealed class CompileTimeConstantRegistry
{
    private readonly IReadOnlyList<CompileTimeConstantSymbol> _constants;
    private readonly CompileTimeSymbolResolver<CompileTimeConstantSymbol> _resolver;
    private readonly CompileTimeScriptTypeRegistry _types;
    private readonly Dictionary<CompileTimeSymbolId, EvaluationState> _states = [];
    private readonly Dictionary<CompileTimeSymbolId, CompileTimeValue> _values = [];
    private readonly List<CompileTimeConstantSymbol> _evaluationStack = [];

    private CompileTimeConstantRegistry(
        IReadOnlyList<CompileTimeConstantSymbol> constants,
        CompileTimeModuleContext modules,
        CompileTimeScriptTypeRegistry types,
        CompileTimeConstantRegistry? previous)
    {
        _constants = constants;
        _resolver = new CompileTimeSymbolResolver<CompileTimeConstantSymbol>(
            constants,
            modules);
        _types = types;

        if (previous is null)
        {
            return;
        }

        foreach (var constant in constants)
        {
            if (previous._states.GetValueOrDefault(constant.Id)
                    == EvaluationState.Evaluated
                && previous._values.TryGetValue(constant.Id, out var value))
            {
                _states[constant.Id] = EvaluationState.Evaluated;
                _values[constant.Id] = value;
            }
        }
    }

    public static CompileTimeConstantRegistry Empty { get; } =
        new(
            [],
            CompileTimeModuleContext.Empty,
            CompileTimeScriptTypeRegistry.Default,
            previous: null);

    public static CompileTimeConstantRegistry Create(
        ProgramNode program,
        CompileTimeModuleContext modules,
        CompileTimeScriptTypeRegistry types,
        CompileTimeConstantRegistry? previous = null)
    {
        var constants = program.CompileTimeConstants
            .Select(constant => CreateSymbol(constant, modules))
            .GroupBy(symbol => symbol.Id)
            .Select(group => group.First())
            .ToList();
        return new CompileTimeConstantRegistry(
            constants,
            modules,
            types,
            previous);
    }

    public CompileTimeSymbolLookup<CompileTimeConstantSymbol> Lookup(
        string requestedName,
        string callerModule) =>
        _resolver.Lookup(requestedName, callerModule);

    public CompileTimeValue? Evaluate(
        CompileTimeConstantSymbol constant,
        Location referenceLocation,
        DiagnosticBag diagnostics,
        Func<CompileTimeConstantSymbol, CompileTimeValue?> evaluateInitializer)
    {
        var state = _states.GetValueOrDefault(constant.Id);
        if (state == EvaluationState.Evaluated)
        {
            return _values[constant.Id];
        }
        if (state == EvaluationState.Failed)
        {
            return null;
        }
        if (state == EvaluationState.Evaluating)
        {
            ReportCycle(constant, referenceLocation, diagnostics);
            return null;
        }

        _states[constant.Id] = EvaluationState.Evaluating;
        _evaluationStack.Add(constant);
        try
        {
            var value = evaluateInitializer(constant);
            if (value is null)
            {
                _states[constant.Id] = EvaluationState.Failed;
                return null;
            }

            if (!_types.Matches(constant.Declaration.TypeNode, value))
            {
                diagnostics.Report(
                    constant.Declaration.Initializer.Location,
                    $"Compile-time constant '{constant.Name}' declares type '{CompileTimeScriptTypeRegistry.Display(constant.Declaration.TypeNode)}' but evaluated to {CompileTimeValueFacts.Describe(value)}.");
                _states[constant.Id] = EvaluationState.Failed;
                return null;
            }

            CompileTimeValueFacts.Freeze(value);
            _values[constant.Id] = value;
            _states[constant.Id] = EvaluationState.Evaluated;
            return value;
        }
        finally
        {
            _evaluationStack.RemoveAt(_evaluationStack.Count - 1);
        }
    }

    public void Validate(DiagnosticBag diagnostics)
    {
        foreach (var constant in _constants)
        {
            if (!_types.IsSupported(constant.Declaration.TypeNode))
            {
                diagnostics.Report(
                    constant.Declaration.TypeNode.Location,
                    $"Compile-time constant '{constant.Name}' uses unsupported type '{CompileTimeScriptTypeRegistry.Display(constant.Declaration.TypeNode)}'.");
            }
        }

        foreach (var duplicate in _constants
            .GroupBy(constant => (
                constant.DeclaringModule,
                constant.Name))
            .Where(group => group.Count() > 1))
        {
            foreach (var constant in duplicate.Skip(1))
            {
                diagnostics.Report(
                    constant.Declaration.Location,
                    $"Compile-time constant '{constant.Name}' is already declared in module '{constant.DeclaringModule}'.");
            }
        }
    }

    private static CompileTimeConstantSymbol CreateSymbol(
        CompileTimeConstantNode constant,
        CompileTimeModuleContext modules)
    {
        var original = modules.TryGetOriginal<CompileTimeConstantNode>(
            constant,
            out var originalConstant)
            ? originalConstant
            : constant;
        var declaringModule = modules.ModuleFor(constant);
        return new CompileTimeConstantSymbol(
            new CompileTimeSymbolId(
                declaringModule,
                original.Name,
                constant.Location.File.Path,
                constant.Location.Position),
            constant,
            original.Name,
            declaringModule,
            original.Visibility);
    }

    private void ReportCycle(
        CompileTimeConstantSymbol repeated,
        Location location,
        DiagnosticBag diagnostics)
    {
        var start = _evaluationStack.FindIndex(constant =>
            constant.Id == repeated.Id);
        var chain = _evaluationStack
            .Skip(Math.Max(start, 0))
            .Select(constant => constant.Name)
            .Append(repeated.Name);
        diagnostics.Report(
            location,
            $"Circular compile-time constant dependency: {string.Join(" -> ", chain)}.");
    }

    private enum EvaluationState
    {
        NotEvaluated,
        Evaluating,
        Evaluated,
        Failed,
    }
}
