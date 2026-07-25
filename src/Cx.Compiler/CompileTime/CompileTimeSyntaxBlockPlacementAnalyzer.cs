using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class CompileTimeSyntaxBlockPlacementAnalyzer(DiagnosticBag diagnostics)
{
    public void Analyze(ProgramNode program)
    {
        foreach (var declaration in program.Declarations)
        {
            AnalyzeTopLevel(declaration);
        }
    }

    private void AnalyzeTopLevel(TopLevelNode declaration)
    {
        switch (declaration)
        {
            case CompileTimeIfTopLevelNode conditional:
                AnalyzeBlock(conditional.ThenBlock, SyntaxBlockPlacement.TopLevel);
                AnalyzeBlock(conditional.ElseBlock, SyntaxBlockPlacement.TopLevel);
                break;
            case CompileTimeForeachTopLevelNode foreachNode:
                AnalyzeBlock(foreachNode.Body, SyntaxBlockPlacement.TopLevel);
                break;
            case MacroDeclarationNode { ExpansionKind: MacroExpansionKind.Statements } macro:
                AnalyzeStatements(macro.Template.Statements);
                break;
            case MacroDeclarationNode macro:
                foreach (var nested in macro.Template.DeclarationNodes)
                {
                    AnalyzeTopLevel(nested);
                }
                break;
            case CDeclareNode cDeclare:
                AnalyzeCDeclareMembers(cDeclare.Members);
                break;
            case FunctionNode function:
                AnalyzeStatements(function.Body);
                break;
            case StructNode structNode:
                AnalyzeFunctions(structNode.Methods);
                break;
            case ExtensionNode extension:
                AnalyzeFunctions(extension.Methods);
                break;
            case TypeAdapterNode adapter:
                AnalyzeFunctions(adapter.Methods);
                break;
            case TaggedUnionNode union:
                AnalyzeFunctions(union.Methods);
                break;
            case TestNode test:
                AnalyzeStatements(test.Body);
                break;
        }
    }

    private void AnalyzeFunctions(IEnumerable<FunctionNode> functions)
    {
        foreach (var function in functions)
        {
            AnalyzeStatements(function.Body);
        }
    }

    private void AnalyzeStatements(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case CompileTimeIfStatementNode conditional:
                    AnalyzeBlock(conditional.ThenBlock, SyntaxBlockPlacement.Statement);
                    AnalyzeBlock(conditional.ElseBlock, SyntaxBlockPlacement.Statement);
                    break;
                case CompileTimeForeachStatementNode foreachNode:
                    AnalyzeBlock(foreachNode.Body, SyntaxBlockPlacement.Statement);
                    break;
                case IfStatement conditional:
                    AnalyzeStatements(conditional.ThenBody);
                    if (conditional.ElseBranch is not null)
                    {
                        AnalyzeStatements([conditional.ElseBranch]);
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    AnalyzeStatements(elseBlock.Body);
                    break;
                case WhileStatement whileStatement:
                    AnalyzeStatements(whileStatement.Body);
                    break;
                case ForStatement forStatement:
                    AnalyzeStatements(forStatement.Body);
                    break;
                case ForeachStatement foreachStatement:
                    AnalyzeStatements(foreachStatement.Body);
                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        AnalyzeStatements(switchCase.Body);
                    }
                    AnalyzeStatements(switchStatement.DefaultBody);
                    break;
                case MatchStatement matchStatement:
                    foreach (var arm in matchStatement.Arms)
                    {
                        AnalyzeStatements(arm.Body);
                    }
                    break;
            }
        }
    }

    private void AnalyzeCDeclareMembers(IEnumerable<SyntaxNode> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case CompileTimeIfDeclarationNode conditional:
                    AnalyzeBlock(conditional.ThenBlock, SyntaxBlockPlacement.CDeclaration);
                    AnalyzeBlock(conditional.ElseBlock, SyntaxBlockPlacement.CDeclaration);
                    break;
                case CompileTimeForeachDeclarationNode foreachNode:
                    AnalyzeBlock(foreachNode.Body, SyntaxBlockPlacement.CDeclaration);
                    break;
            }
        }
    }

    private void AnalyzeBlock(SyntaxBlockNode block, SyntaxBlockPlacement placement)
    {
        foreach (var item in block.Items)
        {
            if (!IsValid(item, placement))
            {
                diagnostics.Report(
                    item.Location,
                    $"Compile-time syntax block item '{item.GetType().Name}' is not valid in " +
                    $"{PlacementDisplayName(placement)} context.");
            }

            switch (placement)
            {
                case SyntaxBlockPlacement.Statement when item is StatementNode statement:
                    AnalyzeStatements([statement]);
                    break;
                case SyntaxBlockPlacement.TopLevel when item is TopLevelNode declaration:
                    AnalyzeTopLevel(declaration);
                    break;
                case SyntaxBlockPlacement.CDeclaration:
                    AnalyzeCDeclareMembers([item]);
                    break;
            }
        }
    }

    private static bool IsValid(SyntaxNode item, SyntaxBlockPlacement placement) =>
        placement switch
        {
            SyntaxBlockPlacement.Statement => item is StatementNode,
            SyntaxBlockPlacement.TopLevel => item is TopLevelNode,
            SyntaxBlockPlacement.CDeclaration => IsCDeclareMember(item),
            _ => false,
        };

    private static bool IsCDeclareMember(SyntaxNode item) => item is
        CLinkNode
        or TypeAliasNode
        or EnumNode
        or StructNode
        or TaggedUnionNode
        or GlobalVariableNode
        or ExternFunctionNode
        or CompileTimeIfDeclarationNode
        or CompileTimeForeachDeclarationNode;

    private static string PlacementDisplayName(SyntaxBlockPlacement placement) =>
        placement switch
        {
            SyntaxBlockPlacement.Statement => "statement",
            SyntaxBlockPlacement.TopLevel => "top-level declaration",
            SyntaxBlockPlacement.CDeclaration => "C declaration",
            _ => "unknown",
        };

    private enum SyntaxBlockPlacement
    {
        Statement,
        TopLevel,
        CDeclaration,
    }
}
