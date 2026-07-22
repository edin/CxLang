namespace Cx.Compiler;

public enum MemberCompletionKind
{
    Field,
    Method,
    EnumMember,
}

public sealed record MemberCompletion(
    string Label,
    MemberCompletionKind Kind,
    string Detail);
