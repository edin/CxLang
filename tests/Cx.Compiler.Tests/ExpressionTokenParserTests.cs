using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class ExpressionTokenParserTests
{
    [Fact]
    public void ParseExpression_UsesOperatorPrecedence()
    {
        var expression = ParseReturnedExpression("left + right * 2");

        var add = Assert.IsType<BinaryExpressionNode>(expression);
        Assert.Equal(BinaryOperator.Add, add.Operator);
        Assert.IsType<NameExpressionNode>(add.Left);

        var multiply = Assert.IsType<BinaryExpressionNode>(add.Right);
        Assert.Equal(BinaryOperator.Multiply, multiply.Operator);
        Assert.IsType<NameExpressionNode>(multiply.Left);
        Assert.IsType<LiteralExpressionNode>(multiply.Right);
    }

    [Fact]
    public void ParseExpression_UsesRightAssociativeAssignment()
    {
        var expression = ParseReturnedExpression("a = b = c");

        var outer = Assert.IsType<AssignmentExpressionNode>(expression);
        Assert.Equal(AssignmentOperator.Assign, outer.Operator);
        Assert.IsType<NameExpressionNode>(outer.Target);

        var inner = Assert.IsType<AssignmentExpressionNode>(outer.Value);
        Assert.Equal(AssignmentOperator.Assign, inner.Operator);
    }

    [Fact]
    public void ParseExpression_ParsesInclusiveScalarRange()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("'a'...'z'");

        var range = Assert.IsType<ScalarRangeExpressionNode>(expression);
        Assert.True(range.IsInclusive);
        Assert.IsType<LiteralExpressionNode>(range.Start);
        Assert.IsType<LiteralExpressionNode>(range.End);
    }

    [Fact]
    public void ParseExpression_ParsesExclusiveScalarRangeFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("0..10");

        var range = Assert.IsType<ScalarRangeExpressionNode>(expression);
        Assert.False(range.IsInclusive);
        Assert.IsType<LiteralExpressionNode>(range.Start);
        Assert.IsType<LiteralExpressionNode>(range.End);
    }

    [Fact]
    public void ParseExpression_ParsesGenericFunctionCall()
    {
        var expression = ParseReturnedExpression("identity<int>(value)");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(call.Callee);
        Assert.Single(call.TypeArgumentNodes);
        Assert.Equal("int", call.TypeArgumentNodes[0].ToSourceText());
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ParseExpression_ParsesGenericOwnerMemberCall()
    {
        var expression = ParseReturnedExpression("Vec<int>.create()");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        var member = Assert.IsType<MemberExpressionNode>(call.Callee);
        Assert.IsType<NameExpressionNode>(member.Target);
        Assert.Equal("create", member.MemberName);
        Assert.Single(call.TypeArgumentNodes);
        Assert.Equal("int", call.TypeArgumentNodes[0].ToSourceText());
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void ParseExpression_ParsesNestedGenericFunctionCallTypeArgument()
    {
        var expression = ParseReturnedExpression("identity<Box<int>>(value)");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        Assert.Single(call.TypeArgumentNodes);
        Assert.Equal("Box<int>", call.TypeArgumentNodes[0].ToSourceText());
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ParseExpression_ParsesDeepNestedGenericFunctionCallFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("identity<Vec<Box<int>>>(value)");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        Assert.Single(call.TypeArgumentNodes);
        Assert.Equal("Vec<Box<int>>", call.TypeArgumentNodes[0].ToSourceText());
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ParseExpression_ParsesStaticGenericOwnerCallWithMultipleTypeArgumentsFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("Map<StringView, Vec<int>>.create()");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        var member = Assert.IsType<MemberExpressionNode>(call.Callee);
        Assert.Equal("create", member.MemberName);
        Assert.Equal(["StringView", "Vec<int>"], call.TypeArgumentNodes.Select(node => node.ToSourceText()).ToList());
    }

    [Fact]
    public void ParseExpression_ParsesGenericFunctionCallWithMultipleTypeArgumentsFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("make_pair<int, Vec<Box<int>>>(left, right)");

        var call = Assert.IsType<GenericCallExpressionNode>(expression);
        Assert.Equal(["int", "Vec<Box<int>>"], call.TypeArgumentNodes.Select(node => node.ToSourceText()).ToList());
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void ParseExpression_ParsesConditionalExpression()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("flag ? left : right");

        var conditional = Assert.IsType<ConditionalExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(conditional.Condition);
        Assert.IsType<NameExpressionNode>(conditional.WhenTrue);
        Assert.IsType<NameExpressionNode>(conditional.WhenFalse);
    }

    [Fact]
    public void ParseExpression_ParsesNestedConditionalAsRightAssociative()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("a ? b : c ? d : e");

        var outer = Assert.IsType<ConditionalExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(outer.WhenTrue);

        var inner = Assert.IsType<ConditionalExpressionNode>(outer.WhenFalse);
        Assert.IsType<NameExpressionNode>(inner.Condition);
        Assert.IsType<NameExpressionNode>(inner.WhenTrue);
        Assert.IsType<NameExpressionNode>(inner.WhenFalse);
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfPrimitiveType()
    {
        var expression = ParseReturnedExpression("sizeof(int)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(Assert.IsType<SizeOfUnresolvedOperandNode>(sizeOf.Operand).ExpressionCandidate);
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfGenericType()
    {
        var expression = ParseReturnedExpression("sizeof(Box<int>)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        Assert.Equal("Box<int>", Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand).TypeNode.ToSourceText());
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfNestedGenericPointerTypeFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("sizeof(Vec<Box<int>>*)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        var operand = Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand);
        Assert.Equal("Vec<Box<int>>*", operand.TypeNode.ToSourceText());
        Assert.IsType<PointerTypeSyntaxNode>(operand.TypeNode.Syntax);
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfFunctionTypeFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("sizeof(fn(int, char*) -> bool)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        var functionType = Assert.IsType<FunctionTypeSyntaxNode>(Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand).TypeNode.Syntax);
        Assert.Equal(2, functionType.Parameters.Count);
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfExpressionOperandFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("sizeof(value + 1)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        Assert.IsType<BinaryExpressionNode>(Assert.IsType<SizeOfExpressionOperandNode>(sizeOf.Operand).Expression);
    }

    [Fact]
    public void ParseExpression_ParsesSizeOfExpressionOperand()
    {
        var expression = ParseReturnedExpression("sizeof(flags)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(Assert.IsType<SizeOfUnresolvedOperandNode>(sizeOf.Operand).ExpressionCandidate);
    }

    [Fact]
    public void ParseExpression_KeepsAmbiguousSizeOfIdentifierUnresolved()
    {
        var expression = ParseReturnedExpression("sizeof(Point)");

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(expression);
        Assert.IsType<NameExpressionNode>(Assert.IsType<SizeOfUnresolvedOperandNode>(sizeOf.Operand).ExpressionCandidate);
    }

    [Fact]
    public void TypeResolution_ResolvesAmbiguousSizeOfIdentifierToTypeWhenNoValueSymbolExists()
    {
        var program = CompilerTestHelpers.Parse(
            """
            struct Point {
                x: int;
            }

            fn main() -> int {
                return sizeof(Point);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(
            Assert.IsType<ReturnStatement>(Assert.Single(program.Functions.Single().Body)).Expression);
        Assert.Equal("Point", Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand).TypeNode.ToSourceText());
    }

    [Fact]
    public void TypeResolution_ResolvesAmbiguousSizeOfBuiltinIdentifierToType()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main() -> int {
                return sizeof(int);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(
            Assert.IsType<ReturnStatement>(Assert.Single(program.Functions.Single().Body)).Expression);
        Assert.Equal("int", Assert.IsType<SizeOfTypeOperandNode>(sizeOf.Operand).TypeNode.ToSourceText());
    }

    [Fact]
    public void TypeResolution_KeepsAmbiguousSizeOfIdentifierAsExpressionWhenValueSymbolExists()
    {
        var program = CompilerTestHelpers.Parse(
            """
            fn main(value: int) -> int {
                return sizeof(value);
            }
            """);
        CompilerTestHelpers.Resolve(program);

        var diagnostics = new DiagnosticBag();
        new TypeResolutionPass(diagnostics).Resolve(program);
        CompilerTestHelpers.AssertNoErrors(diagnostics);

        var sizeOf = Assert.IsType<SizeOfExpressionNode>(
            Assert.IsType<ReturnStatement>(Assert.Single(program.Functions.Single().Body)).Expression);
        Assert.IsType<NameExpressionNode>(Assert.IsType<SizeOfExpressionOperandNode>(sizeOf.Operand).Expression);
    }

    [Fact]
    public void ParseExpression_ParsesTypedInitializer()
    {
        var expression = ParseReturnedExpression("Point { x: 10, y: 20 }");

        var initializer = Assert.IsType<InitializerExpressionNode>(expression);
        Assert.Equal("Point", initializer.TypeNameNode?.ToSourceText());
        Assert.Equal(["x", "y"], initializer.Fields.Select(field => field.Name).ToList());
        Assert.Empty(initializer.Values);
    }

    [Fact]
    public void ParseExpression_ParsesGenericTypedInitializer()
    {
        var expression = ParseReturnedExpression("Box<int> { value: 10 }");

        var initializer = Assert.IsType<InitializerExpressionNode>(expression);
        Assert.Equal("Box<int>", initializer.TypeNameNode?.ToSourceText());
        var field = Assert.Single(initializer.Fields);
        Assert.Equal("value", field.Name);
        Assert.IsType<LiteralExpressionNode>(field.Value);
    }

    [Fact]
    public void ParseExpression_ParsesUntypedInitializerValues()
    {
        var expression = ParseReturnedExpression("{ 1, 2, 3 }");

        var initializer = Assert.IsType<InitializerExpressionNode>(expression);
        Assert.Null(initializer.TypeNameNode);
        Assert.Empty(initializer.Fields);
        Assert.Equal(3, initializer.Values.Count);
    }

    [Fact]
    public void ParseExpression_ParsesDedicatedListExpression()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("[1, [2, 3]]");

        var list = Assert.IsType<ListExpressionNode>(expression);
        Assert.Equal(2, list.Elements.Count);
        Assert.IsType<LiteralExpressionNode>(list.Elements[0]);
        Assert.IsType<ListExpressionNode>(list.Elements[1]);
        Assert.Equal("[1, [2, 3]]", list.ToSourceText());
    }

    [Fact]
    public void ParseExpression_ParsesNestedInitializerFieldsFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("Box<Vec<int>> { value: Vec<int> { data: null, length: 0 } }");

        var initializer = Assert.IsType<InitializerExpressionNode>(expression);
        Assert.Equal("Box<Vec<int>>", initializer.TypeNameNode?.ToSourceText());

        var field = Assert.Single(initializer.Fields);
        Assert.Equal("value", field.Name);

        var nested = Assert.IsType<InitializerExpressionNode>(field.Value);
        Assert.Equal("Vec<int>", nested.TypeNameNode?.ToSourceText());
        Assert.Equal(["data", "length"], nested.Fields.Select(item => item.Name).ToList());
    }

    [Fact]
    public void ParseExpression_ParsesNestedUntypedInitializerValuesFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("{ Point { x: 10, y: 20 }, Point { x: 30, y: 40 } }");

        var initializer = Assert.IsType<InitializerExpressionNode>(expression);
        Assert.Null(initializer.TypeNameNode);
        Assert.Equal(2, initializer.Values.Count);
        Assert.All(initializer.Values, value => Assert.IsType<InitializerExpressionNode>(value));
    }

    [Fact]
    public void ParseExpression_ParsesPrimitiveCast()
    {
        var expression = ParseReturnedExpression("(int)value");

        var cast = Assert.IsType<CastExpressionNode>(expression);
        Assert.Equal("int", cast.TargetTypeNode?.ToSourceText());
        Assert.IsType<NameExpressionNode>(cast.Expression);
    }

    [Fact]
    public void ParseExpression_ParsesGenericPointerCast()
    {
        var expression = ParseReturnedExpression("(Box<int>*)value");

        var cast = Assert.IsType<CastExpressionNode>(expression);
        Assert.Equal("Box<int>*", cast.TargetTypeNode?.ToSourceText());
        Assert.IsType<NameExpressionNode>(cast.Expression);
    }

    [Fact]
    public void ParseExpression_ParsesConstPointerCastFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("(const char*)value");

        var cast = Assert.IsType<CastExpressionNode>(expression);
        Assert.Equal("const char*", cast.TargetTypeNode?.ToSourceText());
        var pointer = Assert.IsType<PointerTypeSyntaxNode>(cast.TargetTypeNode?.Syntax);
        var constType = Assert.IsType<ConstTypeSyntaxNode>(pointer.Element);
        Assert.Equal("char", Assert.IsType<NamedTypeSyntaxNode>(constType.Element).Name);
        Assert.IsType<NameExpressionNode>(cast.Expression);
    }

    [Fact]
    public void ParseExpression_ParsesNestedGenericPointerCastFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("(Vec<Box<int>>*)value");

        var cast = Assert.IsType<CastExpressionNode>(expression);
        Assert.Equal("Vec<Box<int>>*", cast.TargetTypeNode?.ToSourceText());
        Assert.IsType<PointerTypeSyntaxNode>(cast.TargetTypeNode?.Syntax);
    }

    [Fact]
    public void ParseExpression_ParsesCastOperandWithPointerDereferenceFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("(int*)*value");

        var cast = Assert.IsType<CastExpressionNode>(expression);
        Assert.Equal("int*", cast.TargetTypeNode?.ToSourceText());
        Assert.IsType<UnaryExpressionNode>(cast.Expression);
    }

    [Fact]
    public void ParseExpression_FallbackLambdaTypesUseTypeTokenParser()
    {
        var expression = ParseReturnedExpression("fn(value: Box<int>*) -> Vec<Box<int>*> { return Vec<Box<int>*> { data: null, length: 0 }; }");

        var function = Assert.IsType<FunctionExpressionNode>(expression);
        var parameterPointer = Assert.IsType<PointerTypeSyntaxNode>(Assert.Single(function.Parameters).TypeNode?.Syntax);
        Assert.IsType<GenericTypeSyntaxNode>(parameterPointer.Element);

        var returnGeneric = Assert.IsType<GenericTypeSyntaxNode>(function.ReturnTypeNode?.Syntax);
        var boxedPointer = Assert.IsType<PointerTypeSyntaxNode>(Assert.Single(returnGeneric.Arguments));
        Assert.IsType<GenericTypeSyntaxNode>(boxedPointer.Element);
    }

    [Fact]
    public void ParseExpression_KeepsParenthesizedExpression()
    {
        var expression = ParseReturnedExpression("(left + right)");

        var parenthesized = Assert.IsType<ParenthesizedExpressionNode>(expression);
        var binary = Assert.IsType<BinaryExpressionNode>(parenthesized.Expression);
        Assert.Equal(BinaryOperator.Add, binary.Operator);
    }

    [Fact]
    public void ParseExpression_DoesNotTreatParenthesizedMultiplicationAsCast()
    {
        var expression = ParseReturnedExpression("(value * 10)");

        var parenthesized = Assert.IsType<ParenthesizedExpressionNode>(expression);
        var binary = Assert.IsType<BinaryExpressionNode>(parenthesized.Expression);
        Assert.Equal(BinaryOperator.Multiply, binary.Operator);
    }

    [Fact]
    public void ParseExpression_ParsesFunctionExpressionBody()
    {
        var expression = ParseReturnedExpression("fn(value: int) => value + 1");

        var function = Assert.IsType<FunctionExpressionNode>(expression);
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("value", parameter.Name);
        Assert.Equal("int", parameter.TypeNode?.ToSourceText());
        Assert.Null(function.ReturnTypeNode);
        Assert.IsType<BinaryExpressionNode>(function.ExpressionBody);
        Assert.Null(function.BlockBody);
    }

    [Fact]
    public void ParseExpression_ParsesFunctionExpressionReturnType()
    {
        var expression = ParseReturnedExpression("fn(value: int) -> int => value + 1");

        var function = Assert.IsType<FunctionExpressionNode>(expression);
        Assert.Equal("int", function.ReturnTypeNode?.ToSourceText());
        Assert.IsType<BinaryExpressionNode>(function.ExpressionBody);
        Assert.Null(function.BlockBody);
    }

    [Fact]
    public void ParseExpression_ParsesBlockFunctionExpressionFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("fn(value: int) -> int { return value + 1; }");

        var function = Assert.IsType<FunctionExpressionNode>(expression);
        Assert.Null(function.ExpressionBody);
        Assert.NotNull(function.BlockBody);
        Assert.Single(function.BlockBody);
    }

    [Fact]
    public void ParseExpression_ParsesBlockFunctionExpressionStatementsFromTokens()
    {
        var expression = CompilerTestHelpers.ParseTokenExpression("fn(value: int) -> int { let next: int = value + 1; return next; }");

        var function = Assert.IsType<FunctionExpressionNode>(expression);
        Assert.Null(function.ExpressionBody);
        Assert.NotNull(function.BlockBody);
        Assert.Collection(
            function.BlockBody,
            statement => Assert.IsType<LetStatement>(statement),
            statement => Assert.IsType<ReturnStatement>(statement));
    }

    private static ExpressionNode ParseReturnedExpression(string expression)
    {
        var program = CompilerTestHelpers.Parse(
            $$"""
            fn main() -> int {
                return {{expression}};
            }
            """);

        var statement = Assert.Single(program.Functions.Single().Body);
        var returnExpression = Assert.IsType<ReturnStatement>(statement).Expression;
        Assert.NotNull(returnExpression);
        return returnExpression;
    }

}
