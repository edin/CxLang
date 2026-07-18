using Cx.Compiler.Source;

namespace Cx.Compiler.Syntax.Nodes;

public sealed record AttributeDeclarationNode(
    Location Location,
    string Name,
    IReadOnlyList<string> Targets,
    IReadOnlyList<AttributeFieldNode> Fields) : TopLevelNode(Location);

public sealed record AttributeFieldNode(
    Location Location,
    string Name,
    CompileTimeTypeNode TypeNode) : SyntaxNode(Location);

public abstract record CompileTimeTypeNode(Location Location) : SyntaxNode(Location);

public enum CompileTimeScalarType
{
    Boolean,
    Integer,
    String,
    Name,
    Type,
    Syntax,
}

public sealed record CompileTimeScalarTypeNode(
    Location Location,
    CompileTimeScalarType Kind) : CompileTimeTypeNode(Location);

public sealed record CompileTimeListTypeNode(
    Location Location,
    CompileTimeTypeNode ElementType) : CompileTimeTypeNode(Location);

public sealed record CompileTimeErrorTypeNode(Location Location) : CompileTimeTypeNode(Location);

public static class CompileTimeTypeNodeExtensions
{
    public static string ToSourceText(this CompileTimeTypeNode type) => type switch
    {
        CompileTimeScalarTypeNode scalar => scalar.Kind switch
        {
            CompileTimeScalarType.Boolean => "bool",
            CompileTimeScalarType.Integer => "int",
            CompileTimeScalarType.String => "string",
            CompileTimeScalarType.Name => "name",
            CompileTimeScalarType.Type => "type",
            CompileTimeScalarType.Syntax => "syntax",
            _ => "<unknown>",
        },
        CompileTimeListTypeNode list => $"list<{list.ElementType.ToSourceText()}>",
        _ => "<error>",
    };
}

public sealed record AttributeApplicationNode(
    Location Location,
    string Name,
    IReadOnlyList<AttributeArgumentNode> Arguments) : SyntaxNode(Location);

public sealed record AttributeArgumentNode(
    Location Location,
    string? Name,
    ExpressionNode Value) : SyntaxNode(Location);
