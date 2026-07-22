namespace Cx.Compiler;

public enum MemberCompletionKind
{
    Field,
    Method,
}

public sealed record MemberCompletion(
    string Label,
    MemberCompletionKind Kind,
    string Detail);
