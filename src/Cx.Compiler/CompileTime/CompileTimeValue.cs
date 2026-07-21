using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal abstract record CompileTimeValue
{
    public sealed record Null : CompileTimeValue;

    public sealed record Boolean(bool Value) : CompileTimeValue;

    public sealed record Integer(long Value) : CompileTimeValue;

    public sealed record String(string Value) : CompileTimeValue;

    public sealed record Name(string Value) : CompileTimeValue;

    public sealed record Type(TypeRef Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "type";
    }

    public sealed record Module(ReflectedModule Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "module";
    }

    public sealed record Syntax(SyntaxNode Value) : CompileTimeObjectValue
    {
        public override string DisplayType => Value is FunctionNode or ExternFunctionNode
            ? "function declaration"
            : "syntax";
    }

    public sealed record RequirementMatch(
        Cx.Compiler.Semantic.RequirementMatch Value,
        RequirementNode Requirement) : CompileTimeObjectValue
    {
        public override string DisplayType => "requirement match";
    }

    public sealed record ResolvedField(
        Cx.Compiler.Semantic.ResolvedField Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "resolved field";
    }

    public sealed record ResolvedMethod(
        Cx.Compiler.Semantic.ResolvedMethod Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "resolved method";
    }

    public sealed record ResolvedParameter(
        Cx.Compiler.Semantic.ResolvedParameter Value) : CompileTimeObjectValue
    {
        public override string DisplayType => "resolved parameter";
    }

    public sealed record List : CompileTimeObjectValue
    {
        private readonly System.Collections.Generic.List<CompileTimeValue> _values;

        public List(IEnumerable<CompileTimeValue> values)
        {
            _values = values.ToList();
        }

        public IReadOnlyList<CompileTimeValue> Values => _values;

        public override string DisplayType => "list";

        internal void Add(CompileTimeValue value) => _values.Add(value);
    }
}

internal abstract record CompileTimeObjectValue : CompileTimeValue
{
    public abstract string DisplayType { get; }
}

internal static class CompileTimeValueFacts
{
    public static string Describe(CompileTimeValue value) => value switch
    {
        CompileTimeObjectValue objectValue => objectValue.DisplayType,
        CompileTimeValue.Null => "null",
        CompileTimeValue.Boolean => "boolean",
        CompileTimeValue.Integer => "integer",
        CompileTimeValue.String => "string",
        CompileTimeValue.Name => "name",
        _ => "unknown",
    };
}
