using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Tests;

public sealed class CExpressionLowererTests
{
    [Fact]
    public void LowerSimple_LowersBinaryExpressionToCAst()
    {
        var location = TestLocation();
        var expression = new BinaryExpressionNode(
            location,
            new NameExpressionNode(location, "a"),
            "+",
            new LiteralExpressionNode(location, "1"));

        var lowered = new CExpressionLowerer(new TestContext()).LowerSimple(expression);

        var binary = Assert.IsType<CBinaryExpression>(lowered);
        Assert.Equal("+", binary.Operator);
        Assert.IsType<CNameExpression>(binary.Left);
        Assert.IsType<CLiteralExpression>(binary.Right);
    }

    [Fact]
    public void LowerSimple_LowersCastAndSizeOfUsingContextTypeLowering()
    {
        var location = TestLocation();
        var context = new TestContext(typePrefix: "lowered_");
        var lowerer = new CExpressionLowerer(context);

        var cast = lowerer.LowerSimple(new CastExpressionNode(
            location,
            new NameExpressionNode(location, "value"),
            TypeNode.CreateFromText(location, "Vec<int>*")));
        var sizeOf = lowerer.LowerSimple(new SizeOfExpressionNode(
            location,
            ExpressionOperand: null,
            TypeOperandNode: TypeNode.CreateFromText(location, "Vec<int>")));

        Assert.Equal("lowered_Vec_int*", AssertPointerType(Assert.IsType<CCastExpression>(cast).TargetType));
        Assert.Equal("lowered_Vec_int", AssertNamedType(Assert.IsType<CSizeOfTypeExpression>(sizeOf).Type));
    }

    [Fact]
    public void LowerSimple_UsesResolvedTypeRefForTypeExpressions()
    {
        var location = TestLocation();
        var semanticType = new TypeRef.Named("Vec", [new TypeRef.Named("int", [])]);
        var typeNode = new TypeNode(location, "StaleText");
        typeNode.Semantic.Type = semanticType;
        var context = new TestContext(typeRefPrefix: "typed_");
        var lowerer = new CExpressionLowerer(context);

        var cast = lowerer.LowerSimple(new CastExpressionNode(
            location,
            new NameExpressionNode(location, "value"),
            typeNode));
        var sizeOf = lowerer.LowerSimple(new SizeOfExpressionNode(
            location,
            ExpressionOperand: null,
            TypeOperandNode: typeNode));
        var initializer = lowerer.LowerSimple(new InitializerExpressionNode(
            location,
            [],
            [],
            typeNode));

        Assert.Equal("typed_Vec_int", AssertNamedType(Assert.IsType<CCastExpression>(cast).TargetType));
        Assert.Equal("typed_Vec_int", AssertNamedType(Assert.IsType<CSizeOfTypeExpression>(sizeOf).Type));
        Assert.Equal("typed_Vec_int", AssertNamedType(Assert.IsType<CInitializerExpression>(initializer).Type));
    }

    [Fact]
    public void LowerSimple_LowersInitializerAndAssignmentExpressions()
    {
        var location = TestLocation();
        var lowerer = new CExpressionLowerer(new TestContext());

        var initializer = lowerer.LowerSimple(new InitializerExpressionNode(
            location,
            [new InitializerFieldNode("x", new LiteralExpressionNode(location, "1"))],
            [],
            TypeNode.CreateFromText(location, "Point")));
        var assignment = lowerer.LowerSimple(new AssignmentExpressionNode(
            location,
            new NameExpressionNode(location, "value"),
            "=",
            new LiteralExpressionNode(location, "1")));

        Assert.Equal("Point", AssertNamedType(Assert.IsType<CInitializerExpression>(initializer).Type));
        var loweredAssignment = Assert.IsType<CAssignmentExpression>(assignment);
        Assert.Equal("=", loweredAssignment.Operator);
        Assert.IsType<CNameExpression>(loweredAssignment.Target);
        Assert.IsType<CLiteralExpression>(loweredAssignment.Value);
    }

    [Fact]
    public void LowerSimple_LowersMemberExpressionWithFallbackOrContextOverride()
    {
        var location = TestLocation();
        var fallback = new CExpressionLowerer(new TestContext()).LowerSimple(new MemberExpressionNode(
            location,
            new NameExpressionNode(location, "point"),
            "x"));
        var overridden = new CExpressionLowerer(new TestContext(memberOverride: new CNameExpression("POINT_X"))).LowerSimple(new MemberExpressionNode(
            location,
            new NameExpressionNode(location, "point"),
            "x"));

        var member = Assert.IsType<CMemberExpression>(fallback);
        Assert.Equal(".", member.AccessOperator);
        Assert.Equal("x", member.MemberName);
        Assert.Equal("POINT_X", Assert.IsType<CNameExpression>(overridden).Name);
    }

    private static Location TestLocation() =>
        new(new SourceFile("test.cx", string.Empty), Position: 0, Line: 1, Column: 1);

    private static string AssertNamedType(CTypeRef? type) =>
        Assert.IsType<CNamedTypeRef>(type).Name;

    private static string AssertPointerType(CTypeRef? type)
    {
        var pointer = Assert.IsType<CPointerTypeRef>(type);
        return AssertNamedType(pointer.Element) + "*";
    }

    private sealed class TestContext(
        string typePrefix = "",
        string typeRefPrefix = "",
        CExpression? memberOverride = null) : ICExpressionLoweringContext
    {
        public TypeRef? SelfTypeRef => null;

        public CExpression LowerExpression(ExpressionNode expression) =>
            new CExpressionLowerer(this).LowerSimple(expression);

        public CExpression LowerNameExpression(NameExpressionNode name) =>
            new CNameExpression(name.Name);

        public CExpression LowerAddressOfExpression(ExpressionNode operand) =>
            new CUnaryExpression("&", LowerExpression(operand));

        public CTypeRef LowerTypeRef(TypeRef type) =>
            type switch
            {
                TypeRef.Pointer pointer => new CPointerTypeRef(LowerTypeRef(pointer.Element)),
                TypeRef.Named named => new CNamedTypeRef(typeRefPrefix + typePrefix + CTypeLowerer.LowerType(named, [])),
                TypeRef.Alias alias => new CNamedTypeRef(typeRefPrefix + typePrefix + CTypeLowerer.LowerType(alias, [])),
                _ => new CNamedTypeRef(typeRefPrefix + typePrefix + TypeRefFormatter.ToCxString(type)),
            };

        public TypeRef? ResolveType(TypeNode? typeNode) =>
            typeNode?.Semantic.Type is { } type
                ? type
                : typeNode?.ToTypeRef(new TypeRefParser(new ProgramNode(TestLocation(), [])));

        public CExpression? TryWrapAssignmentValue(AssignmentExpressionNode assignment, CExpression value) => null;

        public CExpression? TryLowerMemberExpression(MemberExpressionNode member) => memberOverride;
    }
}
