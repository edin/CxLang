namespace Cx.Compiler.Semantic;

internal sealed class SemanticModel
{
    public Scope RootScope { get; } = new();

    public FunctionCatalog? FunctionCatalog { get; private set; }

    public FunctionCatalog GetOrCreateFunctionCatalog(Cx.Compiler.Syntax.Nodes.ProgramNode program) =>
        FunctionCatalog ??= Cx.Compiler.Semantic.FunctionCatalog.Build(program);
}
