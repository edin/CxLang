using System.Text;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeExpressionEvaluator
{
    private readonly DiagnosticBag _diagnostics;
    private readonly CompileTimeIntrinsicRegistry _intrinsics;
    private readonly CompileTimeObjectRegistry _objects;
    private readonly CompileTimeMethodRegistry _methods;
    private readonly CompileTimePropertyRegistry _properties;
    private readonly CompileTimeFunctionRegistry _functions;
    private readonly CompileTimeConstantRegistry _constants;
    private readonly ICompileTimeReflection _reflection;
    private readonly CompileTimeEvaluationSession _session;
    private int _deferredReferenceCount;

    public CompileTimeExpressionEvaluator(
        DiagnosticBag diagnostics,
        CompileTimeEnvironment environment,
        ICompileTimeReflection? reflection = null)
        : this(
            diagnostics,
            environment.Intrinsics,
            reflection,
            environment.Objects,
            environment.Methods,
            environment.Properties,
            environment.Functions,
            environment.Constants)
    {
    }

    public CompileTimeExpressionEvaluator(
        DiagnosticBag diagnostics,
        CompileTimeIntrinsicRegistry? intrinsics = null,
        ICompileTimeReflection? reflection = null,
        CompileTimeObjectRegistry? objects = null,
        CompileTimeMethodRegistry? methods = null,
        CompileTimePropertyRegistry? properties = null,
        CompileTimeFunctionRegistry? functions = null,
        CompileTimeConstantRegistry? constants = null,
        CompileTimeEvaluationLimits? limits = null)
    {
        _diagnostics = diagnostics;
        _intrinsics = intrinsics ?? CompileTimeIntrinsicRegistry.CreateDefault();
        _reflection = reflection ?? UnavailableCompileTimeReflection.Instance;
        _objects = objects ?? CompileTimeObjectRegistry.CreateDefault();
        _methods = methods ?? CompileTimeMethodRegistry.Default;
        _properties = properties ?? CompileTimePropertyRegistry.Default;
        _functions = functions ?? CompileTimeFunctionRegistry.Empty;
        _constants = constants ?? CompileTimeConstantRegistry.Empty;
        _session = new CompileTimeEvaluationSession(diagnostics, limits);
    }

    public CompileTimeValue? Evaluate(
        ExpressionNode expression,
        CompileTimeEvaluationContext context)
    {
        if (!_session.TryConsumeStep(expression))
        {
            return null;
        }

        return expression switch
        {
            LiteralExpressionNode literal => EvaluateLiteral(literal),
            NameExpressionNode name => EvaluateName(name, context),
            ParenthesizedExpressionNode parenthesized => Evaluate(parenthesized.Expression, context),
            UnaryExpressionNode unary => EvaluateUnary(unary, context),
            BinaryExpressionNode binary => EvaluateBinary(binary, context),
            ConditionalExpressionNode conditional => EvaluateConditional(conditional, context),
            ListExpressionNode list => EvaluateList(list, context),
            TypeLiteralExpressionNode typeLiteral => EvaluateTypeLiteral(typeLiteral),
            InitializerExpressionNode initializer => EvaluateInitializer(initializer, context),
            CallExpressionNode call => EvaluateCall(call, context),
            MemberExpressionNode member => EvaluateMember(member, context),
            ComputedMemberExpressionNode member => EvaluateComputedMember(member, context),
            _ => Unsupported(expression),
        };
    }

    public CompileTimeEvaluationOutcome EvaluateOutcome(
        ExpressionNode expression,
        CompileTimeEvaluationContext context)
    {
        var initialDeferredReferenceCount = _deferredReferenceCount;
        var initialDiagnosticCount = _diagnostics.Count;
        var value = Evaluate(expression, context);
        if (value is not null)
        {
            return new CompileTimeEvaluationOutcome.Value(value);
        }

        return _deferredReferenceCount > initialDeferredReferenceCount
            && _diagnostics.Count == initialDiagnosticCount
                ? new CompileTimeEvaluationOutcome.Deferred()
                : new CompileTimeEvaluationOutcome.Failed();
    }

    public bool IsKnownObject(string name) => _objects.TryGet(name, out _);

    public T WithGeneratedOrigin<T>(
        GeneratedSyntaxOrigin origin,
        Func<T> action) =>
        _session.WithGeneratedOrigin(origin, action);

    private CompileTimeValue? EvaluateTypeLiteral(TypeLiteralExpressionNode typeLiteral)
    {
        if (!_reflection.IsAvailable)
        {
            _diagnostics.Report(
                typeLiteral.Location,
                "Compile-time type literals require type reflection.");
            return null;
        }

        if (!_reflection.TryGetType(typeLiteral.TypeNode, out var type))
        {
            _diagnostics.Report(
                typeLiteral.Location,
                "Could not resolve compile-time type literal.");
            return null;
        }

        return new CompileTimeValue.Type(type);
    }

    private CompileTimeValue? EvaluateMember(
        MemberExpressionNode member,
        CompileTimeEvaluationContext context)
    {
        if (TryEvaluateConstant(member, out var constantValue))
        {
            return constantValue;
        }

        var target = Evaluate(member.Target, context);
        if (target is null)
        {
            return null;
        }

        return EvaluateProperty(target, member.MemberName, member.Location, context);
    }

    private CompileTimeValue? EvaluateComputedMember(
        ComputedMemberExpressionNode member,
        CompileTimeEvaluationContext context)
    {
        var target = Evaluate(member.Target, context);
        var propertyValue = Evaluate(member.MemberName.Expression, context);
        if (target is null || propertyValue is null)
        {
            return null;
        }

        var propertyName = propertyValue switch
        {
            CompileTimeValue.Name name => name.Value,
            CompileTimeValue.String text => text.Value,
            _ => null,
        };
        if (propertyName is null)
        {
            _diagnostics.Report(
                member.MemberName.Location,
                $"Computed compile-time property name must be a name or string, but received {CompileTimeValueFacts.Describe(propertyValue)}.");
            return null;
        }

        return EvaluateProperty(target, propertyName, member.Location, context);
    }

    private CompileTimeValue? EvaluateProperty(
        CompileTimeValue target,
        string propertyName,
        Cx.Compiler.Source.Location location,
        CompileTimeEvaluationContext context)
    {

        if (target is not CompileTimeObjectValue objectValue)
        {
            _diagnostics.Report(
                location,
                $"Compile-time {CompileTimeValueFacts.Describe(target)} value is not object-like and does not have property '{propertyName}'.");
            return null;
        }

        var propertyContext = new CompileTimePropertyContext(
            location,
            _reflection,
            _diagnostics,
            expression => EvaluateOutcome(expression, context));
        var property = _properties.Get(
            objectValue,
            propertyName,
            propertyContext);
        if (property is CompileTimePropertyResult.Found found)
        {
            return found.Value;
        }

        if (property is CompileTimePropertyResult.Missing)
        {
            _diagnostics.Report(
                location,
                $"Compile-time {objectValue.DisplayType} value does not have property '{propertyName}'.");
        }

        return null;
    }

    private CompileTimeValue? EvaluateLiteral(LiteralExpressionNode literal) =>
        literal.Kind switch
        {
            LiteralKind.Boolean => new CompileTimeValue.Boolean(
                string.Equals(literal.LiteralText, "true", StringComparison.Ordinal)),
            LiteralKind.Integer => ParseInteger(literal),
            LiteralKind.String => ParseString(literal),
            LiteralKind.Null => new CompileTimeValue.Null(),
            _ => Unsupported(literal),
        };

    private CompileTimeValue? ParseInteger(LiteralExpressionNode literal)
    {
        if (IntegerLiteralParser.TryParse(literal.LiteralText, out var value)
            && value >= long.MinValue
            && value <= long.MaxValue)
        {
            return new CompileTimeValue.Integer((long)value);
        }

        _diagnostics.Report(literal.Location, $"Invalid compile-time integer literal '{literal.LiteralText}'.");
        return null;
    }

    private CompileTimeValue? ParseString(LiteralExpressionNode literal)
    {
        var text = literal.LiteralText;
        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            _diagnostics.Report(literal.Location, $"Invalid compile-time string literal '{text}'.");
            return null;
        }

        var result = new StringBuilder(text.Length - 2);
        for (var index = 1; index < text.Length - 1; index++)
        {
            var ch = text[index];
            if (ch != '\\')
            {
                result.Append(ch);
                continue;
            }

            if (++index >= text.Length - 1)
            {
                _diagnostics.Report(literal.Location, $"Invalid escape sequence in compile-time string literal '{text}'.");
                return null;
            }

            var escaped = text[index] switch
            {
                '0' => (char?)'\0',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                '"' => '"',
                _ => null,
            };
            if (escaped is null)
            {
                _diagnostics.Report(
                    literal.Location,
                    $"Unsupported escape sequence '\\{text[index]}' in compile-time string literal.");
                return null;
            }

            result.Append(escaped.Value);
        }

        return new CompileTimeValue.String(result.ToString());
    }

    private CompileTimeValue? EvaluateName(
        NameExpressionNode name,
        CompileTimeEvaluationContext context)
    {
        if (context.TryGet(name.Name, out var value))
        {
            return value;
        }

        if (context.IsDeferred(name.Name))
        {
            _deferredReferenceCount++;
            return null;
        }

        if (TryEvaluateConstant(name, out var constantValue))
        {
            return constantValue;
        }

        if (_objects.TryGet(name.Name, out var compileTimeObject))
        {
            return compileTimeObject;
        }

        if (CompileTimeTypeFacts.TryGetKnownType(name.Name, out var knownType))
        {
            return new CompileTimeValue.Type(knownType);
        }

        if (_reflection.IsAvailable
            && _reflection.TryGetEnumType(name.Name, out var enumType))
        {
            return new CompileTimeValue.Type(enumType);
        }

        if (_reflection.IsAvailable
            && _reflection.TryGetRequirement(name.Name, out var requirement))
        {
            return new CompileTimeValue.Syntax(requirement);
        }

        _diagnostics.Report(name.Location, $"Unknown compile-time name '{name.Name}'.");
        return null;
    }

    private bool TryEvaluateConstant(
        ExpressionNode expression,
        out CompileTimeValue? value)
    {
        value = null;
        var requestedName = ExpressionNameFacts.GetQualifiedName(expression);
        if (requestedName is null)
        {
            return false;
        }

        var callerModule = _session.CurrentModule
            ?? _functions.ModuleForPath(expression.Location.File.Path);
        var lookup = _constants.Lookup(requestedName, callerModule);
        switch (lookup)
        {
            case CompileTimeSymbolLookup<CompileTimeConstantSymbol>.NotSymbolReference:
                return false;
            case CompileTimeSymbolLookup<CompileTimeConstantSymbol>.Missing missing
                when expression is NameExpressionNode
                    && missing.SuggestedModule is null:
                return false;
            case CompileTimeSymbolLookup<CompileTimeConstantSymbol>.Missing missing:
                _diagnostics.Report(
                    expression.Location,
                    missing.SuggestedModule is null
                        ? $"Unknown compile-time constant '{missing.RequestedName}'."
                        : $"Unknown compile-time constant '{missing.RequestedName}'. Did you mean to import {missing.SuggestedModule}?");
                return true;
            case CompileTimeSymbolLookup<CompileTimeConstantSymbol>.Inaccessible inaccessible:
                _diagnostics.Report(
                    expression.Location,
                    $"Compile-time constant '{inaccessible.RequestedName}' is private to module '{inaccessible.DeclaringModule}'.");
                return true;
            case CompileTimeSymbolLookup<CompileTimeConstantSymbol>.Candidates candidates:
                if (candidates.Values.Count != 1)
                {
                    _diagnostics.Report(
                        expression.Location,
                        $"Compile-time constant reference '{requestedName}' is ambiguous.");
                    return true;
                }

                var constant = candidates.Values[0];
                value = _constants.Evaluate(
                    constant,
                    expression.Location,
                    _diagnostics,
                    symbol => _session.WithModule(
                        symbol.DeclaringModule,
                        () => Evaluate(
                            symbol.Declaration.Initializer,
                            new CompileTimeEvaluationContext())));
                return true;
            default:
                return false;
        }
    }

    private CompileTimeValue? EvaluateUnary(
        UnaryExpressionNode unary,
        CompileTimeEvaluationContext context)
    {
        var operand = Evaluate(unary.Operand, context);
        if (operand is null)
        {
            return null;
        }

        return (unary.Operator, operand) switch
        {
            (UnaryOperator.LogicalNot, CompileTimeValue.Boolean boolean) =>
                new CompileTimeValue.Boolean(!boolean.Value),
            (UnaryOperator.Plus, CompileTimeValue.Integer integer) => integer,
            (UnaryOperator.Negate, CompileTimeValue.Integer integer) when integer.Value != long.MinValue =>
                new CompileTimeValue.Integer(-integer.Value),
            _ => InvalidUnaryOperand(unary, operand),
        };
    }

    private CompileTimeValue? EvaluateBinary(
        BinaryExpressionNode binary,
        CompileTimeEvaluationContext context)
    {
        if (binary.Operator is BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr)
        {
            return EvaluateLogical(binary, context);
        }

        var left = Evaluate(binary.Left, context);
        var right = Evaluate(binary.Right, context);
        if (left is null || right is null)
        {
            return null;
        }

        return binary.Operator switch
        {
            BinaryOperator.Equal => new CompileTimeValue.Boolean(AreEqual(left, right)),
            BinaryOperator.NotEqual => new CompileTimeValue.Boolean(!AreEqual(left, right)),
            BinaryOperator.LessThan => Compare(binary, left, right, comparison => comparison < 0),
            BinaryOperator.LessThanOrEqual => Compare(binary, left, right, comparison => comparison <= 0),
            BinaryOperator.GreaterThan => Compare(binary, left, right, comparison => comparison > 0),
            BinaryOperator.GreaterThanOrEqual => Compare(binary, left, right, comparison => comparison >= 0),
            _ => Unsupported(binary),
        };
    }

    private CompileTimeValue? EvaluateLogical(
        BinaryExpressionNode binary,
        CompileTimeEvaluationContext context)
    {
        var left = Evaluate(binary.Left, context);
        if (left is not CompileTimeValue.Boolean leftBoolean)
        {
            return left is null ? null : InvalidBinaryOperands(binary, left, null);
        }

        if (binary.Operator == BinaryOperator.LogicalAnd && !leftBoolean.Value)
        {
            return new CompileTimeValue.Boolean(false);
        }

        if (binary.Operator == BinaryOperator.LogicalOr && leftBoolean.Value)
        {
            return new CompileTimeValue.Boolean(true);
        }

        var right = Evaluate(binary.Right, context);
        return right switch
        {
            CompileTimeValue.Boolean rightBoolean => new CompileTimeValue.Boolean(rightBoolean.Value),
            null => null,
            _ => InvalidBinaryOperands(binary, left, right),
        };
    }

    private CompileTimeValue? EvaluateConditional(
        ConditionalExpressionNode conditional,
        CompileTimeEvaluationContext context)
    {
        var condition = Evaluate(conditional.Condition, context);
        if (condition is not CompileTimeValue.Boolean boolean)
        {
            if (condition is not null)
            {
                _diagnostics.Report(
                    conditional.Condition.Location,
                    "Compile-time conditional expression requires a boolean condition.");
            }

            return null;
        }

        return Evaluate(boolean.Value ? conditional.WhenTrue : conditional.WhenFalse, context);
    }

    private CompileTimeValue? EvaluateInitializer(
        InitializerExpressionNode initializer,
        CompileTimeEvaluationContext context)
    {
        if (initializer.TypeNameNode is not null || initializer.Fields.Count > 0)
        {
            _diagnostics.Report(
                initializer.Location,
                "Compile-time lists require an untyped positional initializer.");
            return null;
        }

        var values = new List<CompileTimeValue>(initializer.Values.Count);
        foreach (var expression in initializer.Values)
        {
            var value = Evaluate(expression, context);
            if (value is null)
            {
                return null;
            }

            values.Add(value);
        }

        return new CompileTimeValue.List(values);
    }

    private CompileTimeValue? EvaluateList(
        ListExpressionNode list,
        CompileTimeEvaluationContext context)
    {
        var values = new List<CompileTimeValue>(list.Elements.Count);
        foreach (var element in list.Elements)
        {
            var value = Evaluate(element, context);
            if (value is null)
            {
                return null;
            }

            values.Add(value);
        }

        return new CompileTimeValue.List(values);
    }

    private CompileTimeValue? EvaluateCall(
        CallExpressionNode call,
        CompileTimeEvaluationContext context)
    {
        if (TryEvaluateFunctionCall(call, context, out var functionValue))
        {
            return functionValue;
        }

        if (call.Callee is MemberExpressionNode member)
        {
            return EvaluateMethodCall(call, member, context);
        }

        if (call.Callee is not NameExpressionNode name)
        {
            _diagnostics.Report(
                call.Location,
                "Compile-time calls require a direct intrinsic name.");
            return null;
        }

        var arguments = EvaluateArguments(call.Arguments, context);
        if (arguments is null)
        {
            return null;
        }

        if (!_intrinsics.TryGet(name.Name, out var intrinsic))
        {
            _diagnostics.Report(
                call.Location,
                $"Unknown compile-time intrinsic '{name.Name}'.");
            return null;
        }

        return intrinsic.Invoke(new CompileTimeIntrinsicContext(
            call.Location,
            arguments,
            _reflection,
            _diagnostics,
            expression => EvaluateOutcome(expression, context)));
    }

    private bool TryEvaluateFunctionCall(
        CallExpressionNode call,
        CompileTimeEvaluationContext context,
        out CompileTimeValue? value)
    {
        value = null;
        var requestedName = ExpressionNameFacts.GetQualifiedName(call.Callee);
        if (requestedName is null)
        {
            return false;
        }

        var callerModule = _session.CurrentModule
            ?? _functions.ModuleForPath(call.Location.File.Path);
        var lookup = _functions.Lookup(requestedName, callerModule);
        switch (lookup)
        {
            case CompileTimeSymbolLookup<CompileTimeFunctionSymbol>.NotSymbolReference:
                return false;
            case CompileTimeSymbolLookup<CompileTimeFunctionSymbol>.Missing missing
                when call.Callee is NameExpressionNode
                    && missing.SuggestedModule is null:
                return false;
            case CompileTimeSymbolLookup<CompileTimeFunctionSymbol>.Missing missing:
                _diagnostics.Report(
                    call.Location,
                    missing.SuggestedModule is null
                        ? $"Unknown compile-time function '{missing.RequestedName}'."
                        : $"Unknown compile-time function '{missing.RequestedName}'. Did you mean to import {missing.SuggestedModule}?");
                return true;
            case CompileTimeSymbolLookup<CompileTimeFunctionSymbol>.Inaccessible inaccessible:
                _diagnostics.Report(
                    call.Location,
                    $"Compile-time function '{inaccessible.RequestedName}' is private to module '{inaccessible.DeclaringModule}'.");
                return true;
            case CompileTimeSymbolLookup<CompileTimeFunctionSymbol>.Candidates candidates:
                var arguments = EvaluateArguments(call.Arguments, context);
                if (arguments is null)
                {
                    return true;
                }

                value = EvaluateFunctionCall(
                    call,
                    requestedName,
                    arguments,
                    candidates.Values);
                return true;
            default:
                return false;
        }
    }

    private CompileTimeValue? EvaluateFunctionCall(
        CallExpressionNode call,
        string name,
        IReadOnlyList<CompileTimeValue> arguments,
        IReadOnlyList<CompileTimeFunctionSymbol> visibleFunctions)
    {
        var arityCandidates = visibleFunctions
            .Where(function =>
                function.Declaration.Parameters.Count == arguments.Count)
            .ToList();
        var candidates = arityCandidates
            .Where(function => function.Declaration.Parameters
                .Select(parameter => parameter.TypeNode)
                .Zip(arguments)
                .All(pair => _functions.Types.Matches(pair.First, pair.Second)))
            .ToList();

        if (candidates.Count != 1)
        {
            var argumentTypes = string.Join(
                ", ",
                arguments.Select(CompileTimeValueFacts.Describe));
            _diagnostics.Report(
                call.Location,
                candidates.Count > 1
                    ? $"Compile-time call '{name}({argumentTypes})' is ambiguous."
                    : $"No compile-time function '{name}' accepts ({argumentTypes}).");
            return null;
        }

        var functionSymbol = candidates[0];
        if (!_session.TryEnterFunction(functionSymbol, call))
        {
            return null;
        }

        var function = functionSymbol.Declaration;
        var functionContext = new CompileTimeEvaluationContext();
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            functionContext.Define(
                function.Parameters[index].Name,
                arguments[index],
                isMutable: false,
                declaredType: function.Parameters[index].TypeNode);
        }

        var firstDiagnosticIndex = _diagnostics.Count;
        try
        {
            var execution = new CompileTimeFunctionInterpreter(
                    _diagnostics,
                    _functions.Types,
                    Evaluate,
                    _session)
                .Execute(function.Body, functionContext);
            if (execution is not CompileTimeFunctionExecution.Returned returned)
            {
                if (execution is CompileTimeFunctionExecution.Completed)
                {
                    _diagnostics.Report(
                        function.Location,
                        $"Compile-time function '{function.Name}' completed without returning a value.");
                }
                else if (execution is CompileTimeFunctionExecution.Break or CompileTimeFunctionExecution.Continue)
                {
                    _diagnostics.Report(
                        function.Location,
                        "'break' and 'continue' are only valid inside a compile-time foreach loop.");
                }

                return null;
            }

            if (!_functions.Types.Matches(function.ReturnTypeNode, returned.Value))
            {
                _diagnostics.Report(
                    returned.Location,
                    $"Compile-time function '{function.Name}' declares return type '{CompileTimeScriptTypeRegistry.Display(function.ReturnTypeNode)}' but returned {CompileTimeValueFacts.Describe(returned.Value)}.");
                return null;
            }

            return returned.Value;
        }
        finally
        {
            _session.AnnotateNewErrors(firstDiagnosticIndex);
            _session.ExitFunction();
        }
    }

    private CompileTimeValue? EvaluateMethodCall(
        CallExpressionNode call,
        MemberExpressionNode member,
        CompileTimeEvaluationContext context)
    {
        var target = Evaluate(member.Target, context);
        if (target is null)
        {
            return null;
        }

        if (target is not CompileTimeObjectValue objectValue)
        {
            _diagnostics.Report(
                member.Location,
                $"Compile-time {CompileTimeValueFacts.Describe(target)} value is not object-like and does not have method '{member.MemberName}'.");
            return null;
        }

        var arguments = EvaluateArguments(call.Arguments, context);
        if (arguments is null)
        {
            return null;
        }

        var result = _methods.Invoke(
            objectValue,
            member.MemberName,
            arguments,
            new CompileTimeMethodContext(call.Location, _reflection, _diagnostics));
        if (result is CompileTimeMethodResult.Invoked invoked)
        {
            return invoked.Value;
        }

        if (result is CompileTimeMethodResult.Missing)
        {
            _diagnostics.Report(
                member.Location,
                $"Compile-time {objectValue.DisplayType} value does not have method '{member.MemberName}'.");
        }

        return null;
    }

    private List<CompileTimeValue>? EvaluateArguments(
        IReadOnlyList<ExpressionNode> argumentExpressions,
        CompileTimeEvaluationContext context)
    {
        var arguments = new List<CompileTimeValue>(argumentExpressions.Count);
        foreach (var argumentExpression in argumentExpressions)
        {
            var argument = Evaluate(argumentExpression, context);
            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
        }

        return arguments;
    }

    private CompileTimeValue? Compare(
        BinaryExpressionNode binary,
        CompileTimeValue left,
        CompileTimeValue right,
        Func<int, bool> predicate)
    {
        var comparison = (left, right) switch
        {
            (CompileTimeValue.Integer leftInteger, CompileTimeValue.Integer rightInteger) =>
                leftInteger.Value.CompareTo(rightInteger.Value),
            (CompileTimeValue.String leftString, CompileTimeValue.String rightString) =>
                string.Compare(leftString.Value, rightString.Value, StringComparison.Ordinal),
            _ => (int?)null,
        };

        return comparison is { } value
            ? new CompileTimeValue.Boolean(predicate(value))
            : InvalidBinaryOperands(binary, left, right);
    }

    private static bool AreEqual(CompileTimeValue left, CompileTimeValue right) =>
        (left, right) switch
        {
            (CompileTimeValue.Boolean a, CompileTimeValue.Boolean b) => a.Value == b.Value,
            (CompileTimeValue.Integer a, CompileTimeValue.Integer b) => a.Value == b.Value,
            (CompileTimeValue.String a, CompileTimeValue.String b) =>
                string.Equals(a.Value, b.Value, StringComparison.Ordinal),
            (CompileTimeValue.Name a, CompileTimeValue.Name b) =>
                string.Equals(a.Value, b.Value, StringComparison.Ordinal),
            (CompileTimeValue.Type a, CompileTimeValue.Type b) =>
                TypeIdentity.ResolvedEquals(a.Value, b.Value),
            (CompileTimeValue.Null, CompileTimeValue.Null) => true,
            _ => false,
        };

    private CompileTimeValue? InvalidUnaryOperand(
        UnaryExpressionNode unary,
        CompileTimeValue operand)
    {
        _diagnostics.Report(
            unary.Location,
            $"Compile-time operator '{unary.Operator.ToSourceText()}' does not support {CompileTimeValueFacts.Describe(operand)} values.");
        return null;
    }

    private CompileTimeValue? InvalidBinaryOperands(
        BinaryExpressionNode binary,
        CompileTimeValue left,
        CompileTimeValue? right)
    {
        var types = right is null
            ? CompileTimeValueFacts.Describe(left)
            : $"{CompileTimeValueFacts.Describe(left)} and {CompileTimeValueFacts.Describe(right)}";
        _diagnostics.Report(
            binary.Location,
            $"Compile-time operator '{binary.Operator.ToSourceText()}' does not support {types} values.");
        return null;
    }

    private CompileTimeValue? Unsupported(ExpressionNode expression)
    {
        _diagnostics.Report(
            expression.Location,
            $"Expression node '{expression.GetType().Name}' is not supported during compile-time evaluation.");
        return null;
    }

}

internal abstract record CompileTimeEvaluationOutcome
{
    public sealed record Value(CompileTimeValue Result) : CompileTimeEvaluationOutcome;

    public sealed record Deferred : CompileTimeEvaluationOutcome;

    public sealed record Failed : CompileTimeEvaluationOutcome;
}
