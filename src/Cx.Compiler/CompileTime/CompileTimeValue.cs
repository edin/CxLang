using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal abstract record CompileTimeValue
{
    public sealed record Boolean(bool Value) : CompileTimeValue;

    public sealed record Integer(long Value) : CompileTimeValue;

    public sealed record String(string Value) : CompileTimeValue;

    public sealed record Name(string Value) : CompileTimeValue;

    public sealed record Type(TypeRef Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "type";

        public override CompileTimePropertyResult GetProperty(
            string name,
            CompileTimePropertyContext context) =>
            CompileTimeObjectProperties.GetTypeProperty(this, name, context);
    }

    public sealed record Syntax(SyntaxNode Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "syntax";

        public override CompileTimePropertyResult GetProperty(
            string name,
            CompileTimePropertyContext context) =>
            CompileTimeObjectProperties.GetSyntaxProperty(this, name, context);
    }

    public sealed record RequirementMatch(
        Cx.Compiler.Semantic.RequirementMatch Value,
        RequirementNode Requirement) : CompileTimeObjectValue
    {
        public override string DisplayType => "requirement match";

        public override CompileTimePropertyResult GetProperty(
            string name,
            CompileTimePropertyContext context) =>
            CompileTimeObjectProperties.GetRequirementMatchProperty(this, name, context);
    }

    public sealed record List(IReadOnlyList<CompileTimeValue> Values) : CompileTimeObjectValue
    {
        public override string DisplayType => "list";

        public override CompileTimePropertyResult GetProperty(
            string name,
            CompileTimePropertyContext context) =>
            CompileTimeObjectProperties.GetListProperty(this, name, context);
    }
}

internal abstract record CompileTimeObjectValue : CompileTimeValue
{
    public abstract string DisplayType { get; }

    public abstract CompileTimePropertyResult GetProperty(
        string name,
        CompileTimePropertyContext context);
}

internal static class CompileTimeValueFacts
{
    public static string Describe(CompileTimeValue value) => value switch
    {
        CompileTimeObjectValue objectValue => objectValue.DisplayType,
        CompileTimeValue.Boolean => "boolean",
        CompileTimeValue.Integer => "integer",
        CompileTimeValue.String => "string",
        CompileTimeValue.Name => "name",
        _ => "unknown",
    };
}
