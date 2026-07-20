using System.Globalization;
using System.Text;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeExpressionEvaluator
{
    private readonly DiagnosticBag _diagnostics;
    private readonly CompileTimeIntrinsicRegistry _intrinsics;
    private readonly CompileTimeObjectRegistry _objects;
    private readonly CompileTimeMethodRegistry _methods;
    private readonly ICompileTimeReflection _reflection;

    public CompileTimeExpressionEvaluator(
        DiagnosticBag diagnostics,
        CompileTimeIntrinsicRegistry? intrinsics = null,
        ICompileTimeReflection? reflection = null,
        CompileTimeObjectRegistry? objects = null,
        CompileTimeMethodRegistry? methods = null)
    {
        _diagnostics = diagnostics;
        _intrinsics = intrinsics ?? CompileTimeIntrinsicRegistry.CreateDefault();
        _reflection = reflection ?? UnavailableCompileTimeReflection.Instance;
        _objects = objects ?? CompileTimeObjectRegistry.CreateDefault();
        _methods = methods ?? CompileTimeMethodRegistry.Default;
    }

    public CompileTimeValue? Evaluate(
        ExpressionNode expression,
        CompileTimeEvaluationContext context) =>
        expression switch
        {
            LiteralExpressionNode literal => EvaluateLiteral(literal),
            NameExpressionNode name => EvaluateName(name, context),
            ParenthesizedExpressionNode parenthesized => Evaluate(parenthesized.Expression, context),
            UnaryExpressionNode unary => EvaluateUnary(unary, context),
            BinaryExpressionNode binary => EvaluateBinary(binary, context),
            ConditionalExpressionNode conditional => EvaluateConditional(conditional, context),
            ListExpressionNode list => EvaluateList(list, context),
            InitializerExpressionNode initializer => EvaluateInitializer(initializer, context),
            CallExpressionNode call => EvaluateCall(call, context),
            MemberExpressionNode member => EvaluateMember(member, context),
            _ => Unsupported(expression),
        };

    private CompileTimeValue? EvaluateMember(
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
                $"Compile-time {CompileTimeValueFacts.Describe(target)} value is not object-like and does not have property '{member.MemberName}'.");
            return null;
        }

        var property = objectValue.GetProperty(
            member.MemberName,
            new CompileTimePropertyContext(
                member.Location,
                _reflection,
                _diagnostics,
                expression => Evaluate(expression, context)));
        if (property is CompileTimePropertyResult.Found found)
        {
            return found.Value;
        }

        if (property is CompileTimePropertyResult.Missing)
        {
            _diagnostics.Report(
                member.Location,
                $"Compile-time {objectValue.DisplayType} value does not have property '{member.MemberName}'.");
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
            _ => Unsupported(literal),
        };

    private CompileTimeValue? ParseInteger(LiteralExpressionNode literal)
    {
        var text = literal.LiteralText.Replace("_", string.Empty, StringComparison.Ordinal);
        try
        {
            var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(text[2..], 16)
                : text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt64(text[2..], 2)
                    : long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new CompileTimeValue.Integer(value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            _diagnostics.Report(literal.Location, $"Invalid compile-time integer literal '{literal.LiteralText}'.");
            return null;
        }
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

        if (_objects.TryGet(name.Name, out var compileTimeObject))
        {
            return compileTimeObject;
        }

        if (CompileTimeTypeFacts.TryGetKnownType(name.Name, out var knownType))
        {
            return new CompileTimeValue.Type(knownType);
        }

        if (_reflection.IsAvailable
            && _reflection.TryGetRequirement(name.Name, out var requirement))
        {
            return new CompileTimeValue.Syntax(requirement);
        }

        _diagnostics.Report(name.Location, $"Unknown compile-time name '{name.Name}'.");
        return null;
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

        if (!_intrinsics.TryGet(name.Name, out var intrinsic))
        {
            _diagnostics.Report(
                call.Location,
                $"Unknown compile-time intrinsic '{name.Name}'.");
            return null;
        }

        var arguments = EvaluateArguments(call.Arguments, context);
        if (arguments is null)
        {
            return null;
        }

        return intrinsic.Invoke(new CompileTimeIntrinsicContext(
            call.Location,
            arguments,
            _reflection,
            _diagnostics,
            expression => Evaluate(expression, context)));
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
