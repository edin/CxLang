namespace Cx.Compiler.Semantic;

internal sealed class SemanticModel
{
    private Cx.Compiler.Syntax.Nodes.ProgramNode? _declarationProgram;

    public Scope RootScope { get; } = new();

    public FunctionCatalog? FunctionCatalog { get; private set; }

    public ProgramDeclarationIndex? DeclarationIndex { get; private set; }

    public FunctionCatalog GetOrCreateFunctionCatalog(Cx.Compiler.Syntax.Nodes.ProgramNode program) =>
        FunctionCatalog ??= Cx.Compiler.Semantic.FunctionCatalog.Build(program);

    public ProgramDeclarationIndex GetOrCreateDeclarationIndex(
        Cx.Compiler.Syntax.Nodes.ProgramNode program)
    {
        if (!ReferenceEquals(_declarationProgram, program))
        {
            _declarationProgram = program;
            DeclarationIndex = ProgramDeclarationIndex.Create(program);
        }

        return DeclarationIndex!;
    }
}
