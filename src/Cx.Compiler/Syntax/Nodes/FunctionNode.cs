using Cx.Compiler.Semantic;
using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

[Flags]
public enum FunctionModifiers
{
    None = 0,
    Static = 1 << 0,
    Implicit = 1 << 1,
    CompileTime = 1 << 2,
}

public sealed record FunctionNode(
    Location Location,
    string Name,
    IReadOnlyList<string> TypeParameters,
    IReadOnlyList<GenericConstraintNode> GenericConstraints,
    IReadOnlyList<ParameterNode> Parameters,
    IReadOnlyList<StatementNode> Body,
    IReadOnlyList<AttributeApplicationNode> Attributes,
    TypeNode? ReturnTypeNode = null,
    TypeNode? OwnerTypeNode = null,
    PlaceholderExpressionNode? ComputedName = null,
    PlaceholderExpressionNode? ComputedParameters = null) : TopLevelNode(Location)
{
    public IReadOnlyList<TypeNode> TypeArgumentNodes { get; init; } = [];

    public IReadOnlyList<string> ReceiverTypeParameters { get; init; } = [];

    public IReadOnlyList<string> MethodTypeParameters =>
        TypeParameters.Skip(ReceiverTypeParameters.Count).ToList();

    public OperatorKind? OperatorKind { get; init; }

    public FunctionModifiers Modifiers { get; internal set; }

    internal FunctionSymbol? FunctionSymbol { get; set; }

    public bool IsStatic => Modifiers.HasFlag(FunctionModifiers.Static);

    public bool IsImplicit => Modifiers.HasFlag(FunctionModifiers.Implicit);

    public bool IsCompileTime => Modifiers.HasFlag(FunctionModifiers.CompileTime);
}
