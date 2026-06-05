namespace Cx.Compiler.Semantic;

internal enum SymbolKind
{
    Type,
    Function,
    Global,
    Parameter,
    Local,
    ForeachBinding,
    MatchBinding
}
