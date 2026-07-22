using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class ScopeResolver(DiagnosticBag diagnostics, SemanticModel model)
{
    private IReadOnlyList<FunctionNode> _functions = [];
    private ProgramNode? _program;
    private ExpressionTypeResolver? _expressionTypeResolver;
    private TypeRefParser? _typeRefParser;

    public void Resolve(ProgramNode program)
    {
        _program = program;
        _functions = GetAllFunctions(program);
        _expressionTypeResolver = new ExpressionTypeResolver(program);
        _typeRefParser = new TypeRefParser(program);
        DeclareTopLevelSymbols(program);

        foreach (var function in program.Functions)
        {
            ResolveFunction(function);
        }

        foreach (var structNode in program.Structs)
        {
            foreach (var method in structNode.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            foreach (var method in union.Methods)
            {
                ResolveFunction(method);
            }
        }
    }

    private void DeclareTopLevelSymbols(ProgramNode program)
    {
        foreach (var typeAlias in program.TypeAliases)
        {
            DeclareTopLevel(typeAlias.Name, SymbolKind.Type, typeAlias.TargetTypeNode, typeAlias.Location, typeAlias);
        }

        foreach (var requirement in program.Requirements)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(requirement.Name, requirement.Location, requirement));
        }

        foreach (var enumNode in program.Enums)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(enumNode.Name, enumNode.Location, enumNode));
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(interfaceNode.Name, interfaceNode.Location, interfaceNode));
        }

        foreach (var structNode in program.Structs)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(structNode.Name, structNode.Location, structNode));
        }

        foreach (var adapter in program.TypeAdapters)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(adapter.Name, adapter.Location, adapter));
        }

        foreach (var union in program.TaggedUnions)
        {
            DeclareTopLevel(CreateNamedTypeSymbol(union.Name, union.Location, union));
        }

        foreach (var global in program.GlobalVariables)
        {
            DeclareTopLevel(global.Name, SymbolKind.Global, global.TypeNode, global.Location, global);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            DeclareTopLevel(externFunction.Name, SymbolKind.Function, externFunction.ReturnTypeNode, externFunction.Location, externFunction);
        }

        foreach (var function in _functions)
        {
            var symbol = CreateSymbol(function.Name, SymbolKind.Function, function.ReturnTypeNode, function.Location, function);
            function.Semantic.Symbol = symbol;
            if (OwnerType(function) is null)
            {
                model.RootScope.TryDeclare(symbol);
            }
        }
    }

    private void ResolveFunction(FunctionNode function)
    {
        var functionScope = model.RootScope.CreateChild();
        var ownerType = TypeRefOrNull(function.OwnerTypeNode);
        if (!function.IsStatic
            && ownerType is not null
            && !function.Parameters.Any(parameter => string.Equals(parameter.Name, "self", StringComparison.Ordinal)))
        {
            Declare(
                functionScope,
                "self",
                SymbolKind.Parameter,
                new TypeRef.Pointer(ownerType).ToTypeNode(function.Location),
                function.Location);
        }

        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            Declare(functionScope, parameter.Name, SymbolKind.Parameter, parameter.TypeNode, parameter.Location, parameter);
        }

        ResolveStatements(function.Body, functionScope);
    }

    private void ResolveStatements(IReadOnlyList<StatementNode> statements, Scope scope)
    {
        foreach (var statement in statements)
        {
            ResolveStatement(statement, scope);
        }
    }

    private void ResolveStatement(StatementNode statement, Scope scope)
    {
        switch (statement)
        {
            case LetStatement let:
                ResolveExpression(let.Initializer, scope);
                Declare(scope, let.Name, SymbolKind.Local, let.TypeNode, let.Location, let);
                break;

            case ReturnStatement { Expression: not null } ret:
                ResolveExpression(ret.Expression, scope);
                break;

            case CStatement c:
                ResolveExpression(c.Expression, scope);
                break;

            case IfStatement ifStatement:
                ResolveExpression(ifStatement.Condition, scope);
                ResolveStatements(ifStatement.ThenBody, scope.CreateChild());
                if (ifStatement.ElseBranch is not null)
                {
                    ResolveStatement(ifStatement.ElseBranch, scope.CreateChild());
                }

                break;

            case ElseBlockStatement elseBlock:
                ResolveStatements(elseBlock.Body, scope);
                break;

            case WhileStatement whileStatement:
                ResolveExpression(whileStatement.Condition, scope);
                ResolveStatements(whileStatement.Body, scope.CreateChild());
                break;

            case ForStatement forStatement:
                var forScope = scope.CreateChild();
                ResolveOptionalForInitializer(forStatement.CachedRangeEndInitializer, forScope);
                ResolveOptionalForInitializer(forStatement.CounterInitializer, forScope);
                ResolveForInitializer(forStatement.Initializer, forScope);
                ResolveExpression(forStatement.Condition, forScope);
                ResolveExpression(forStatement.Increment, forScope);
                ResolveExpression(forStatement.CounterIncrement, forScope);
                ResolveStatements(forStatement.Body, forScope.CreateChild());
                break;

            case ForeachStatement foreachStatement:
                ResolveExpression(foreachStatement.IterableExpression, scope);
                var foreachScope = scope.CreateChild();
                DeclareForeachBinding(foreachScope, foreachStatement.IndexBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.KeyBinding);
                DeclareForeachBinding(foreachScope, foreachStatement.ValueBinding);
                ResolveStatements(foreachStatement.Body, foreachScope);
                break;

            case SwitchStatement switchStatement:
                ResolveExpression(switchStatement.Expression, scope);
                foreach (var switchCase in switchStatement.Cases)
                {
                    ResolveExpression(switchCase.Pattern, scope);
                    ResolveStatements(switchCase.Body, scope.CreateChild());
                }

                ResolveStatements(switchStatement.DefaultBody, scope.CreateChild());
                break;

            case MatchStatement matchStatement:
                ResolveExpression(matchStatement.Expression, scope);
                foreach (var arm in matchStatement.Arms)
                {
                    var armScope = scope.CreateChild();
                    if (arm.BindingName is not null)
                    {
                        Declare(
                            armScope,
                            Symbol.FromTypeRef(arm.BindingName, SymbolKind.MatchBinding, typeRef: null, arm.Location, arm),
                            arm.Location);
                    }

                    ResolveStatements(arm.Body, armScope);
                }

                break;
        }
    }

    private void ResolveForInitializer(ForInitializerNode initializer, Scope scope)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                ResolveExpression(declaration.Initializer, scope);
                Declare(scope, declaration.Name, SymbolKind.Local, declaration.TypeNode, declaration.Location, declaration);
                break;
            case ForExpressionInitializerNode expression:
                ResolveExpression(expression.Expression, scope);
                break;
        }
    }

    private void ResolveOptionalForInitializer(ForInitializerNode? initializer, Scope scope)
    {
        if (initializer is not null)
        {
            ResolveForInitializer(initializer, scope);
        }
    }

    private void DeclareForeachBinding(Scope scope, ForeachBinding? binding)
    {
        if (binding is null)
        {
            return;
        }

        Declare(scope, binding.Name, SymbolKind.ForeachBinding, binding.TypeNode, binding.Location, binding);
    }

    private void ResolveExpression(ExpressionNode? expression, Scope scope)
    {
        switch (expression)
        {
            case null:
                return;
            case NameExpressionNode name:
                if (scope.TryResolve(name.Name, out var symbol))
                {
                    name.Semantic.Symbol = symbol;
                }

                break;
            case ParenthesizedExpressionNode parenthesized:
                ResolveExpression(parenthesized.Expression, scope);
                break;
            case CastExpressionNode cast:
                ResolveExpression(cast.Expression, scope);
                break;
            case UnaryExpressionNode unary:
                ResolveExpression(unary.Operand, scope);
                break;
            case PostfixExpressionNode postfix:
                ResolveExpression(postfix.Operand, scope);
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                ResolveExpression(operand.Expression, scope);
                break;
            case SizeOfExpressionNode { Operand: SizeOfUnresolvedOperandNode { ExpressionCandidate: not null } operand }:
                ResolveExpression(operand.ExpressionCandidate, scope);
                break;
            case BinaryExpressionNode binary:
                ResolveExpression(binary.Left, scope);
                ResolveExpression(binary.Right, scope);
                break;
            case ConditionalExpressionNode conditional:
                ResolveExpression(conditional.Condition, scope);
                ResolveExpression(conditional.WhenTrue, scope);
                ResolveExpression(conditional.WhenFalse, scope);
                break;
            case TryExpressionNode attempt:
                ResolveExpression(attempt.Expression, scope);
                ResolveExpression(attempt.Fallback, scope);
                break;
            case ScalarRangeExpressionNode range:
                ResolveExpression(range.Start, scope);
                ResolveExpression(range.End, scope);
                break;
            case InitializerExpressionNode initializer:
                foreach (var field in initializer.Fields)
                {
                    ResolveExpression(field.Value, scope);
                }

                foreach (var value in initializer.Values)
                {
                    ResolveExpression(value, scope);
                }

                break;
            case FunctionExpressionNode function:
                var functionScope = scope.CreateChild();
                foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
                {
                    Declare(functionScope, parameter.Name, SymbolKind.Parameter, parameter.TypeNode, parameter.Location, parameter);
                }

                ResolveExpression(function.ExpressionBody, functionScope);
                if (function.BlockBody is not null)
                {
                    ResolveStatements(function.BlockBody, functionScope);
                }

                break;
            case AssignmentExpressionNode assignment:
                ResolveExpression(assignment.Target, scope);
                ResolveExpression(assignment.Value, scope);
                break;
            case CallExpressionNode call:
                ResolveExpression(call.Callee, scope);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, scope);
                }

                BindCall(call, scope);
                break;
            case GenericCallExpressionNode call:
                ResolveExpression(call.Callee, scope);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, scope);
                }

                BindGenericCall(call, scope);
                break;
            case MemberExpressionNode member:
                ResolveExpression(member.Target, scope);
                BindMemberReference(member, scope);
                break;
            case IndexExpressionNode index:
                ResolveExpression(index.Target, scope);
                ResolveExpression(index.Index, scope);
                break;
        }
    }

    private Symbol? Declare(Scope scope, string name, SymbolKind kind, TypeNode? typeNode, Location location, SyntaxNode? node = null) =>
        Declare(scope, CreateSymbol(name, kind, typeNode, location, node), location);

    private Symbol? Declare(Scope scope, Symbol symbol, Location location)
    {
        if (scope.TryDeclare(symbol))
        {
            if (symbol.Node is not null)
            {
                symbol.Node.Semantic.Symbol = symbol;
            }

            return symbol;
        }

        diagnostics.Report(location, $"Duplicate {Describe(symbol.Kind)} '{symbol.Name}' in the same scope.");
        return null;
    }

    private void DeclareTopLevel(string name, SymbolKind kind, TypeNode? typeNode, Location location, SyntaxNode node) =>
        DeclareTopLevel(CreateSymbol(name, kind, typeNode, location, node));

    private void DeclareTopLevel(Symbol symbol)
    {
        if (model.RootScope.TryDeclare(symbol))
        {
            symbol.Node!.Semantic.Symbol = symbol;
        }
    }

    private void BindCall(CallExpressionNode call, Scope scope)
    {
        if (TryBindResolvedCall(call, call.Callee, [], call.Arguments, scope))
        {
            return;
        }

        if (call.Callee is NameExpressionNode name
            && scope.TryResolve(name.Name, out var symbol)
            && symbol.Kind == SymbolKind.Function)
        {
            call.Semantic.Symbol = symbol;
            name.Semantic.Symbol = symbol;
            if (symbol.Node is FunctionNode function)
            {
                call.Semantic.ResolvedCall = CreateResolvedCallInfo(function, Array.Empty<TypeRef>(), isInstance: false);
            }

            return;
        }

        if (call.Callee is MemberExpressionNode member
            && ResolveMemberFunction(member, scope, typeArgumentRefs: Array.Empty<TypeRef>()) is { } resolved)
        {
            var functionSymbol = FunctionSymbol(resolved.Function);
            call.Semantic.Symbol = functionSymbol;
            call.Semantic.ResolvedCall = CreateResolvedCallInfo(resolved.Function, resolved.TypeArgumentRefs, resolved.IsInstance);
            member.Semantic.Symbol = functionSymbol;
            member.Semantic.ResolvedCall = call.Semantic.ResolvedCall;
        }
    }

    private void BindGenericCall(GenericCallExpressionNode call, Scope scope)
    {
        var typeArgumentRefs = TypeArgumentRefs(call.TypeArgumentNodes);
        if (TryBindResolvedCall(call, call.Callee, typeArgumentRefs, call.Arguments, scope))
        {
            return;
        }

        if (call.Callee is NameExpressionNode name
            && FindFreeFunction(name.Name, typeArgumentRefs) is { } function)
        {
            var symbol = FunctionSymbol(function);
            call.Semantic.Symbol = symbol;
            call.Semantic.ResolvedCall = CreateResolvedCallInfo(function, typeArgumentRefs, isInstance: false);
            name.Semantic.Symbol = symbol;
            return;
        }

        if (call.Callee is MemberExpressionNode member
            && ResolveMemberFunction(member, scope, typeArgumentRefs) is { } resolved)
        {
            var functionSymbol = FunctionSymbol(resolved.Function);
            call.Semantic.Symbol = functionSymbol;
            call.Semantic.ResolvedCall = CreateResolvedCallInfo(resolved.Function, resolved.TypeArgumentRefs, resolved.IsInstance);
            member.Semantic.Symbol = functionSymbol;
            member.Semantic.ResolvedCall = call.Semantic.ResolvedCall;
        }
    }

    private bool TryBindResolvedCall(
        ExpressionNode callExpression,
        ExpressionNode callee,
        IReadOnlyList<TypeRef> typeArguments,
        IReadOnlyList<ExpressionNode> arguments,
        Scope scope)
    {
        if (_program is null || _expressionTypeResolver is null)
        {
            return false;
        }

        var resolver = new CallResolver(_program, _expressionTypeResolver.ResolveTypeRef);
        var variables = BuildTypeEnvironment(scope);
        var resolved = resolver.ResolveTypeRefs(callee, typeArguments, arguments, variables);
        if (resolved?.Function is null)
        {
            return false;
        }

        var functionSymbol = FunctionSymbol(resolved.Function);
        callExpression.Semantic.Symbol = functionSymbol;
        callExpression.Semantic.ResolvedCall = CreateResolvedCallInfo(
            resolved.Function,
            resolved.TypeArgumentRefs,
            resolved.IsInstance);

        switch (callee)
        {
            case NameExpressionNode name:
                name.Semantic.Symbol = functionSymbol;
                break;
            case MemberExpressionNode member:
                member.Semantic.Symbol = functionSymbol;
                member.Semantic.ResolvedCall = callExpression.Semantic.ResolvedCall;
                break;
        }

        return true;
    }

    private TypeEnvironment BuildTypeEnvironment(Scope scope)
    {
        var environment = new TypeEnvironment();
        for (var current = scope; current is not null; current = current.Parent)
        {
            foreach (var symbol in current.Symbols.Values)
            {
                if (symbol.Kind is SymbolKind.Function or SymbolKind.Type
                    || environment.Types.ContainsKey(symbol.Name))
                {
                    continue;
                }

                if (symbol.TypeRef is not null)
                {
                    environment.Set(symbol.Name, symbol.TypeRef);
                }
            }
        }

        return environment;
    }

    private Symbol CreateSymbol(string name, SymbolKind kind, TypeNode? typeNode, Location location, SyntaxNode? node)
    {
        var typeRef = TypeRefOrNull(typeNode);
        return Symbol.FromTypeRef(
            name,
            kind,
            typeRef,
            location,
            node);
    }

    private static Symbol CreateNamedTypeSymbol(string name, Location location, SyntaxNode node) =>
        Symbol.FromTypeRef(
            name,
            SymbolKind.Type,
            new TypeRef.Named(name, []),
            location,
            node);

    private void BindMemberReference(MemberExpressionNode member, Scope scope)
    {
        if (ResolveMemberFunction(member, scope, typeArgumentRefs: Array.Empty<TypeRef>()) is { } resolved)
        {
            var functionSymbol = FunctionSymbol(resolved.Function);
            member.Semantic.Symbol = functionSymbol;
            member.Semantic.ResolvedCall = CreateResolvedCallInfo(resolved.Function, resolved.TypeArgumentRefs, resolved.IsInstance);
        }
    }

    private static ResolvedCallInfo CreateResolvedCallInfo(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArgumentRefs,
        bool isInstance) =>
        new(function, typeArgumentRefs, isInstance);

    private ResolvedFunction? ResolveMemberFunction(
        MemberExpressionNode member,
        Scope scope,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is not null
            && FindModuleFunction(targetName, member.MemberName, typeArgumentRefs) is { } moduleFunction)
        {
            return new ResolvedFunction(moduleFunction, ResolveFunctionTypeArgumentRefs(moduleFunction, typeArgumentRefs), IsInstance: false);
        }

        if (targetName is not null
            && FindStaticFunction(targetName, member.MemberName, typeArgumentRefs) is { } staticFunction)
        {
            return new ResolvedFunction(staticFunction, ResolveFunctionTypeArgumentRefs(staticFunction, typeArgumentRefs), IsInstance: false);
        }

        if (member.Target is NameExpressionNode receiver
            && scope.TryResolve(receiver.Name, out var receiverSymbol)
            && FindInstanceFunction(receiverSymbol, member.MemberName, typeArgumentRefs) is { } instanceFunction)
        {
            var receiverTypeArguments = ResolveFunctionTypeArgumentRefs(instanceFunction, typeArgumentRefs, receiverSymbol);
            return new ResolvedFunction(
                instanceFunction,
                receiverTypeArguments,
                IsInstance: true);
        }

        return null;
    }

    private FunctionNode? FindFreeFunction(string name, IReadOnlyList<TypeRef> typeArgumentRefs) =>
        _functions.FirstOrDefault(function =>
            OwnerType(function) is null
            && string.Equals(function.Name, name, StringComparison.Ordinal)
            && MatchesTypeArguments(function, typeArgumentRefs));

    private FunctionNode? FindStaticFunction(string ownerType, string name, IReadOnlyList<TypeRef> typeArgumentRefs) =>
        _functions.FirstOrDefault(function =>
            function.IsStatic
            && OwnerType(function) is not null
            && string.Equals(OwnerType(function), ownerType, StringComparison.Ordinal)
            && string.Equals(function.Name, name, StringComparison.Ordinal)
            && MatchesTypeArguments(function, typeArgumentRefs));

    private FunctionNode? FindModuleFunction(string moduleName, string name, IReadOnlyList<TypeRef> typeArgumentRefs) =>
        _functions.FirstOrDefault(function =>
            OwnerType(function) is null
            && string.Equals(function.Semantic.ModuleName, moduleName, StringComparison.Ordinal)
            && string.Equals(function.Name, name, StringComparison.Ordinal)
            && MatchesTypeArguments(function, typeArgumentRefs));

    private FunctionNode? FindInstanceFunction(Symbol receiverSymbol, string name, IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        if (receiverSymbol.TypeRef is not null)
        {
            var ownerType = TypeRefFacts.GetBaseName(receiverSymbol.TypeRef);
            if (!string.IsNullOrWhiteSpace(ownerType))
            {
                return _functions.FirstOrDefault(function =>
                    !function.IsStatic
                    && OwnerType(function) is not null
                    && string.Equals(OwnerType(function), ownerType, StringComparison.Ordinal)
                    && string.Equals(function.Name, name, StringComparison.Ordinal)
                    && MatchesTypeArguments(function, typeArgumentRefs, receiverSymbol.TypeRef));
            }
        }

        return null;
    }

    private static bool MatchesTypeArguments(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArgumentRefs)
    {
        if (function.TypeParameters.Count == 0)
        {
            return typeArgumentRefs.Count == 0;
        }

        if (typeArgumentRefs.Count > 0)
        {
            return typeArgumentRefs.Count == function.TypeParameters.Count;
        }

        return false;
    }

    private static bool MatchesTypeArguments(
        FunctionNode function,
        IReadOnlyList<TypeRef> typeArgumentRefs,
        TypeRef? receiverType)
    {
        if (function.TypeParameters.Count == 0)
        {
            return typeArgumentRefs.Count == 0;
        }

        if (typeArgumentRefs.Count > 0)
        {
            return typeArgumentRefs.Count == function.TypeParameters.Count;
        }

        return TypeRefFacts.TryGetGenericArguments(receiverType, out var receiverArguments)
            && receiverArguments.Count == function.TypeParameters.Count;
    }

    private IReadOnlyList<TypeRef> ResolveFunctionTypeArgumentRefs(
        FunctionNode function,
        IReadOnlyList<TypeRef> explicitTypeArgumentRefs)
    {
        if (function.TypeParameters.Count == 0)
        {
            return [];
        }

        if (explicitTypeArgumentRefs.Count > 0)
        {
            return explicitTypeArgumentRefs;
        }

        return [];
    }

    private IReadOnlyList<TypeRef> ResolveFunctionTypeArgumentRefs(
        FunctionNode function,
        IReadOnlyList<TypeRef> explicitTypeArgumentRefs,
        Symbol receiverSymbol)
    {
        if (function.TypeParameters.Count == 0)
        {
            return [];
        }

        if (explicitTypeArgumentRefs.Count > 0)
        {
            return explicitTypeArgumentRefs;
        }

        if (receiverSymbol.TypeRef is not null
            && TypeRefFacts.TryGetGenericArguments(receiverSymbol.TypeRef, out var receiverArguments)
            && receiverArguments.Count == function.TypeParameters.Count)
        {
            return receiverArguments;
        }

        return [];
    }

    private Symbol FunctionSymbol(FunctionNode function)
    {
        if (function.Semantic.Symbol is { Kind: SymbolKind.Function } symbol)
        {
            return symbol;
        }

        symbol = CreateSymbol(function.Name, SymbolKind.Function, function.ReturnTypeNode, function.Location, function);
        function.Semantic.Symbol = symbol;
        return symbol;
    }

    private static IReadOnlyList<FunctionNode> GetAllFunctions(ProgramNode program) =>
        program.Functions
            .Concat(program.Structs.SelectMany(structNode => structNode.Methods))
            .Concat(program.TaggedUnions.SelectMany(union => union.Methods))
            .Distinct()
            .ToList();

    private sealed record ResolvedFunction(
        FunctionNode Function,
        IReadOnlyList<TypeRef> TypeArgumentRefs,
        bool IsInstance);

    private string? OwnerType(FunctionNode function) =>
        TypeRefFacts.GetBaseName(TypeRefOrNull(function.OwnerTypeNode));

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode> nodes) =>
        nodes.Select(TypeRefOrUnknown).ToList();

    private TypeRef TypeRefOrUnknown(TypeNode typeNode) =>
        TypeRefOrNull(typeNode) ?? new TypeRef.Unknown();

    private TypeRef? TypeRefOrNull(TypeNode? typeNode)
    {
        if (typeNode is null)
        {
            return null;
        }

        if (_typeRefParser is null)
        {
            throw new InvalidOperationException("Scope resolver has no TypeRef parser.");
        }

        var type = typeNode.ToTypeRef(_typeRefParser);
        return type is TypeRef.Unknown ? null : type;
    }

    private static string Describe(SymbolKind kind) =>
        kind switch
        {
            SymbolKind.Type => "type",
            SymbolKind.Function => "function",
            SymbolKind.Global => "global",
            SymbolKind.Parameter => "parameter",
            SymbolKind.Local => "local",
            SymbolKind.ForeachBinding => "foreach binding",
            SymbolKind.MatchBinding => "match binding",
            _ => "symbol"
        };
}
