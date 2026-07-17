using Cx.Compiler.Syntax;

namespace Cx.Compiler.Semantic;

internal sealed class SemanticInfo
{
    public TypeRef? Type { get; set; }

    public Symbol? Symbol { get; set; }

    public SyntaxNode? Origin { get; set; }

    public string? ModuleName { get; set; }

    public ResolvedCallInfo? ResolvedCall { get; set; }

    public bool IsScopeCleanup { get; set; }

    public SemanticInfo Clone() =>
        new()
        {
            Type = Type,
            Symbol = Symbol,
            Origin = Origin,
            ModuleName = ModuleName,
            ResolvedCall = ResolvedCall,
            IsScopeCleanup = IsScopeCleanup,
        };
}
