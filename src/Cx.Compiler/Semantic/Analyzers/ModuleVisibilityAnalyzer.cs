using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class ModuleVisibilityAnalyzer(
    DiagnosticBag diagnostics,
    IReadOnlyList<ProgramNode> availablePrograms)
{
    private readonly IReadOnlyDictionary<string, ModuleSymbols> _modules = availablePrograms
        .GroupBy(program => program.Module?.Name ?? string.Empty, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => ModuleSymbols.From(group),
            StringComparer.Ordinal);

    public void Analyze(IReadOnlyList<ProgramNode> userPrograms)
    {
        foreach (var group in userPrograms.GroupBy(program => program.Module?.Name ?? string.Empty, StringComparer.Ordinal))
        {
            var module = group.Key;
            var visibility = ModuleVisibility.From(module, group, _modules);
            foreach (var program in group)
            {
                AnalyzeProgram(program, visibility);
            }
        }
    }

    private void AnalyzeProgram(ProgramNode program, ModuleVisibility visibility)
    {
        foreach (var typeAlias in program.TypeAliases)
        {
            AnalyzeType(typeAlias.TargetTypeNode, typeAlias.Location, visibility);
            AnalyzePublicType(typeAlias.TargetTypeNode, typeAlias.Location, visibility, typeAlias.IsPublic);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            AnalyzeType(externFunction.ReturnTypeNode, externFunction.Location, visibility);
            AnalyzePublicType(externFunction.ReturnTypeNode, externFunction.Location, visibility, externFunction.IsPublic);
            foreach (var parameter in externFunction.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                AnalyzePublicType(parameter.TypeNode, parameter.Location, visibility, externFunction.IsPublic);
            }
        }

        foreach (var global in program.GlobalVariables)
        {
            AnalyzeType(global.TypeNode, global.Location, visibility);
            AnalyzePublicType(global.TypeNode, global.Location, visibility, global.IsPublic);
            AnalyzeExpression(global.Initializer, visibility);
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var field in structNode.Fields)
            {
                AnalyzeType(field.TypeNode, field.Location, visibility, structNode.TypeParameters);
                AnalyzePublicType(
                    field.TypeNode,
                    field.Location,
                    visibility,
                    structNode.IsPublic,
                    structNode.TypeParameters);
            }

            foreach (var method in structNode.Methods)
            {
                AnalyzeFunction(method, visibility, structNode.IsPublic);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var variant in union.Variants)
            {
                AnalyzeType(variant.TypeNode, variant.Location, visibility);
                AnalyzePublicType(variant.TypeNode, variant.Location, visibility, union.IsPublic);
            }

            foreach (var method in union.Methods)
            {
                AnalyzeFunction(method, visibility, union.IsPublic);
            }
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            foreach (var method in interfaceNode.Methods)
            {
                AnalyzeType(method.ReturnTypeNode, method.Location, visibility);
                AnalyzePublicType(method.ReturnTypeNode, method.Location, visibility, interfaceNode.IsPublic);
                foreach (var parameter in method.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                    AnalyzePublicType(parameter.TypeNode, parameter.Location, visibility, interfaceNode.IsPublic);
                }
            }
        }

        foreach (var function in program.Functions)
        {
            AnalyzeFunction(function, visibility, function.IsPublic);
        }
    }

    private void AnalyzeFunction(FunctionNode function, ModuleVisibility visibility, bool isPublicApi = false)
    {
        AnalyzeType(function.ReturnTypeNode, function.Location, visibility, function.TypeParameters);
        AnalyzePublicType(
            function.ReturnTypeNode,
            function.Location,
            visibility,
            isPublicApi,
            function.TypeParameters);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            AnalyzeType(parameter.TypeNode, parameter.Location, visibility, function.TypeParameters);
            AnalyzePublicType(
                parameter.TypeNode,
                parameter.Location,
                visibility,
                isPublicApi,
                function.TypeParameters);
        }

        var locals = new HashSet<string>(function.Parameters.Select(parameter => parameter.Name), StringComparer.Ordinal);
        foreach (var local in CollectLocalNames(function.Body))
        {
            locals.Add(local);
        }

        AnalyzeStatements(function.Body, visibility, locals);
    }

    private void AnalyzeStatements(
        IReadOnlyList<StatementNode> statements,
        ModuleVisibility visibility,
        IReadOnlySet<string> locals)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    AnalyzeType(let.TypeNode, let.Location, visibility);
                    AnalyzeExpression(let.Initializer, visibility, locals);
                    break;
                case ReturnStatement { Expression: not null } returnStatement:
                    AnalyzeExpression(returnStatement.Expression, visibility, locals);
                    break;
                case IfStatement ifStatement:
                    AnalyzeExpression(ifStatement.Condition, visibility, locals);
                    AnalyzeStatements(ifStatement.ThenBody, visibility, locals);
                    if (ifStatement.ElseBranch is not null)
                    {
                        AnalyzeStatements([ifStatement.ElseBranch], visibility, locals);
                    }

                    break;
                case ElseBlockStatement elseBlock:
                    AnalyzeStatements(elseBlock.Body, visibility, locals);
                    break;
                case WhileStatement whileStatement:
                    AnalyzeExpression(whileStatement.Condition, visibility, locals);
                    AnalyzeStatements(whileStatement.Body, visibility, locals);
                    break;
                case ForStatement forStatement:
                    AnalyzeForInitializer(forStatement.CachedRangeEndInitializer, visibility, locals);
                    AnalyzeForInitializer(forStatement.CounterInitializer, visibility, locals);
                    AnalyzeForInitializer(forStatement.Initializer, visibility, locals);
                    AnalyzeExpression(forStatement.Condition, visibility, locals);
                    AnalyzeExpression(forStatement.Increment, visibility, locals);
                    AnalyzeExpression(forStatement.CounterIncrement, visibility, locals);
                    AnalyzeStatements(forStatement.Body, visibility, locals);
                    break;
                case ForeachStatement foreachStatement:
                    AnalyzeExpression(foreachStatement.IterableExpression, visibility, locals);
                    AnalyzeStatements(foreachStatement.Body, visibility, AddForeachLocals(locals, foreachStatement));
                    break;
                case SwitchStatement switchStatement:
                    AnalyzeExpression(switchStatement.Expression, visibility, locals);
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        AnalyzeExpression(switchCase.Pattern, visibility, locals);
                        AnalyzeStatements(switchCase.Body, visibility, locals);
                    }

                    AnalyzeStatements(switchStatement.DefaultBody, visibility, locals);
                    break;
                case MatchStatement matchStatement:
                    AnalyzeExpression(matchStatement.Expression, visibility, locals);
                    foreach (var arm in matchStatement.Arms)
                    {
                        AnalyzeStatements(arm.Body, visibility, locals);
                    }

                    break;
                case CStatement cStatement:
                    AnalyzeExpression(cStatement.Expression, visibility, locals);
                    break;
            }
        }
    }

    private void AnalyzeForInitializer(
        ForInitializerNode? initializer,
        ModuleVisibility visibility,
        IReadOnlySet<string> locals)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                AnalyzeType(declaration.TypeNode, declaration.Location, visibility);
                AnalyzeExpression(declaration.Initializer, visibility, locals);
                break;
            case ForExpressionInitializerNode expression:
                AnalyzeExpression(expression.Expression, visibility, locals);
                break;
        }
    }

    private void AnalyzeExpression(
        ExpressionNode? expression,
        ModuleVisibility visibility,
        IReadOnlySet<string>? locals = null)
    {
        if (expression is null)
        {
            return;
        }

        switch (expression)
        {
            case NameExpressionNode name:
                AnalyzeName(name, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                break;
            case ParenthesizedExpressionNode parenthesized:
                AnalyzeExpression(parenthesized.Expression, visibility, locals);
                break;
            case CastExpressionNode cast:
                AnalyzeType(cast.TargetTypeNode, cast.Location, visibility);
                AnalyzeExpression(cast.Expression, visibility, locals);
                break;
            case UnaryExpressionNode unary:
                AnalyzeExpression(unary.Operand, visibility, locals);
                break;
            case PostfixExpressionNode postfix:
                AnalyzeExpression(postfix.Operand, visibility, locals);
                break;
            case SizeOfExpressionNode { Operand: SizeOfTypeOperandNode operand } sizeOf:
                AnalyzeType(operand.TypeNode, sizeOf.Location, visibility);
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                AnalyzeExpression(operand.Expression, visibility, locals);
                break;
            case SizeOfExpressionNode { Operand: SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } operand }:
                AnalyzeExpression(operand.ExpressionCandidate, visibility, locals);
                break;
            case BinaryExpressionNode binary:
                AnalyzeExpression(binary.Left, visibility, locals);
                AnalyzeExpression(binary.Right, visibility, locals);
                break;
            case ScalarRangeExpressionNode range:
                AnalyzeExpression(range.Start, visibility, locals);
                AnalyzeExpression(range.End, visibility, locals);
                break;
            case ConditionalExpressionNode conditional:
                AnalyzeExpression(conditional.Condition, visibility, locals);
                AnalyzeExpression(conditional.WhenTrue, visibility, locals);
                AnalyzeExpression(conditional.WhenFalse, visibility, locals);
                break;
            case InitializerExpressionNode initializer:
                if (initializer.TypeNameNode is not null)
                {
                    AnalyzeType(initializer.TypeNameNode, initializer.Location, visibility);
                }

                foreach (var field in initializer.Fields)
                {
                    AnalyzeExpression(field.Value, visibility, locals);
                }

                foreach (var value in initializer.Values)
                {
                    AnalyzeExpression(value, visibility, locals);
                }

                break;
            case FunctionExpressionNode function:
                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    AnalyzeType(parameter.TypeNode, parameter.Location, visibility);
                }

                if (function.ReturnTypeNode is not null)
                {
                    AnalyzeType(function.ReturnTypeNode, function.Location, visibility);
                }

                AnalyzeExpression(function.ExpressionBody, visibility, locals);
                break;
            case AssignmentExpressionNode assignment:
                AnalyzeExpression(assignment.Target, visibility, locals);
                AnalyzeExpression(assignment.Value, visibility, locals);
                break;
            case CallExpressionNode call:
                AnalyzeCall(call.Callee, call.Location, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, visibility, locals);
                }

                break;
            case GenericCallExpressionNode call:
                foreach (var typeArgument in call.TypeArgumentNodes)
                {
                    AnalyzeType(typeArgument, call.Location, visibility);
                }

                AnalyzeCall(call.Callee, call.Location, visibility, locals ?? new HashSet<string>(StringComparer.Ordinal));
                foreach (var argument in call.Arguments)
                {
                    AnalyzeExpression(argument, visibility, locals);
                }

                break;
            case MemberExpressionNode member:
                if (ExpressionNameFacts.GetQualifiedName(member) is not { } qualifiedName)
                {
                    AnalyzeExpression(member.Target, visibility, locals);
                    break;
                }

                if (visibility.IsVisibleFunction(qualifiedName)
                    || visibility.IsVisibleValue(qualifiedName)
                    || visibility.IsVisibleType(qualifiedName))
                {
                    break;
                }

                if (visibility.SymbolExistsAsValue(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildValueDiagnostic(qualifiedName));
                }
                else if (visibility.SymbolExistsAsFunction(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildFunctionDiagnostic(qualifiedName));
                }
                else if (visibility.SymbolExistsAsType(qualifiedName))
                {
                    diagnostics.Report(member.Location, visibility.BuildTypeDiagnostic(qualifiedName));
                }
                else
                {
                    AnalyzeExpression(member.Target, visibility, locals);
                }

                break;
            case IndexExpressionNode index:
                AnalyzeExpression(index.Target, visibility, locals);
                AnalyzeExpression(index.Index, visibility, locals);
                break;
        }
    }

    private void AnalyzeCall(
        ExpressionNode callee,
        Location location,
        ModuleVisibility visibility,
        IReadOnlySet<string> locals)
    {
        if (ExpressionNameFacts.GetQualifiedName(callee) is not { } name || locals.Contains(name))
        {
            AnalyzeExpression(callee, visibility, locals);
            return;
        }

        if (!visibility.SymbolExistsAsFunction(name) || visibility.IsVisibleFunction(name))
        {
            AnalyzeExpression(callee, visibility, locals);
            return;
        }

        diagnostics.Report(location, visibility.BuildFunctionDiagnostic(name));
    }

    private void AnalyzeName(
        NameExpressionNode name,
        ModuleVisibility visibility,
        IReadOnlySet<string> locals)
    {
        if (locals.Contains(name.Name)
            || !visibility.SymbolExistsAsValue(name.Name)
            || visibility.IsVisibleValue(name.Name))
        {
            return;
        }

        diagnostics.Report(name.Location, visibility.BuildValueDiagnostic(name.Name));
    }

    private void AnalyzeType(
        TypeNode? typeNode,
        Location location,
        ModuleVisibility visibility,
        IReadOnlyList<string>? typeParameters = null)
    {
        foreach (var typeName in FindTypeNames(typeNode)
            .Where(typeName => typeParameters is null || !typeParameters.Contains(typeName, StringComparer.Ordinal)))
        {
            if (!visibility.SymbolExistsAsType(typeName) || visibility.IsVisibleType(typeName))
            {
                continue;
            }

            diagnostics.Report(location, visibility.BuildTypeDiagnostic(typeName));
        }
    }

    private void AnalyzePublicType(
        TypeNode? typeNode,
        Location location,
        ModuleVisibility visibility,
        bool isPublicApi,
        IReadOnlyList<string>? typeParameters = null)
    {
        if (!isPublicApi)
        {
            return;
        }

        foreach (var typeName in FindTypeNames(typeNode)
            .Where(typeName => typeParameters is null || !typeParameters.Contains(typeName, StringComparer.Ordinal))
            .Where(visibility.IsPrivateTypeInCurrentModule))
        {
            diagnostics.Report(location, $"Public declaration exposes private type '{typeName}'.");
        }
    }

    private static IEnumerable<string> CollectLocalNames(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement let:
                    yield return let.Name;
                    break;
                case ForStatement { Initializer: ForDeclarationInitializerNode declaration }:
                    yield return declaration.Name;
                    foreach (var local in CollectLocalNames(GetBody(statement)))
                    {
                        yield return local;
                    }

                    break;
                case ForeachStatement foreachStatement:
                    foreach (var local in GetForeachBindingNames(foreachStatement))
                    {
                        yield return local;
                    }

                    foreach (var local in CollectLocalNames(foreachStatement.Body))
                    {
                        yield return local;
                    }

                    break;
                default:
                    foreach (var local in CollectLocalNames(GetBody(statement)))
                    {
                        yield return local;
                    }

                    break;
            }
        }
    }

    private static IReadOnlyList<StatementNode> GetBody(StatementNode statement) => statement switch
    {
        IfStatement ifStatement => ifStatement.ThenBody
            .Concat(ifStatement.ElseBranch is null ? [] : [ifStatement.ElseBranch])
            .ToList(),
        ElseBlockStatement elseBlock => elseBlock.Body,
        WhileStatement whileStatement => whileStatement.Body,
        ForStatement forStatement => forStatement.Body,
        ForeachStatement foreachStatement => foreachStatement.Body,
        SwitchStatement switchStatement => switchStatement.Cases
            .SelectMany(switchCase => switchCase.Body)
            .Concat(switchStatement.DefaultBody)
            .ToList(),
        MatchStatement matchStatement => matchStatement.Arms.SelectMany(arm => arm.Body).ToList(),
        _ => [],
    };

    private static IReadOnlySet<string> AddForeachLocals(IReadOnlySet<string> locals, ForeachStatement foreachStatement)
    {
        var scoped = locals.ToHashSet(StringComparer.Ordinal);
        foreach (var name in GetForeachBindingNames(foreachStatement))
        {
            scoped.Add(name);
        }

        return scoped;
    }

    private static IEnumerable<string> GetForeachBindingNames(ForeachStatement foreachStatement)
    {
        if (foreachStatement.IndexBinding is not null)
        {
            yield return foreachStatement.IndexBinding.Name;
        }

        if (foreachStatement.KeyBinding is not null)
        {
            yield return foreachStatement.KeyBinding.Name;
        }

        yield return foreachStatement.ValueBinding.Name;
    }

    private static IReadOnlyList<string> FindTypeNames(TypeNode? typeNode) =>
        FindTypeNames(typeNode?.Syntax);

    private static IReadOnlyList<string> FindTypeNames(TypeSyntaxNode? syntax)
    {
        var names = new List<string>();
        CollectTypeNames(syntax, names);
        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectTypeNames(TypeSyntaxNode? syntax, List<string> names)
    {
        switch (syntax)
        {
            case null:
                break;
            case NamedTypeSyntaxNode named:
                names.Add(NormalizeTypeName(named.Name));
                break;
            case GenericTypeSyntaxNode generic:
                CollectTypeNames(generic.Target, names);
                foreach (var argument in generic.Arguments)
                {
                    CollectTypeNames(argument, names);
                }
                break;
            case PointerTypeSyntaxNode pointer:
                CollectTypeNames(pointer.Element, names);
                break;
            case ConstTypeSyntaxNode constType:
                CollectTypeNames(constType.Element, names);
                break;
            case FixedArrayTypeSyntaxNode fixedArray:
                CollectTypeNames(fixedArray.Element, names);
                break;
            case FunctionTypeSyntaxNode function:
                foreach (var parameter in function.Parameters)
                {
                    CollectTypeNames(parameter, names);
                }
                CollectTypeNames(function.ReturnType, names);
                break;
        }
    }

    private static string NormalizeTypeName(string type)
    {
        type = BuiltinTypes.Normalize(type);
        return BuiltinTypes.IsBuiltin(type) ? string.Empty : type;
    }

    private static string? OwnerType(FunctionNode function)
    {
        var type = function.OwnerTypeNode?.Semantic.Type;
        return type is null or TypeRef.Unknown
            ? null
            : TypeRefFacts.GetBaseName(type);
    }

    private sealed record ModuleVisibility(
        string ModuleName,
        IReadOnlyDictionary<string, ModuleSymbols> Modules,
        IReadOnlySet<string> BareModules,
        IReadOnlyDictionary<string, string> Aliases,
        IReadOnlyDictionary<string, ImportedSymbol> SymbolImports)
    {
        public static ModuleVisibility From(
            string moduleName,
            IEnumerable<ProgramNode> programs,
            IReadOnlyDictionary<string, ModuleSymbols> modules)
        {
            var imports = programs.SelectMany(program => program.Imports).ToList();
            var symbolImports = programs.SelectMany(program => program.SymbolImports).ToList();
            return new ModuleVisibility(
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

    private sealed record ImportedSymbol(string VisibleName, string SourceName, string ModuleName);

    private sealed record ModuleSymbols(
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
    }
}
