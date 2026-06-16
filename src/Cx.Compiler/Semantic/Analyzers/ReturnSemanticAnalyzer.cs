using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class ReturnSemanticAnalyzer(
    DiagnosticBag diagnostics,
    AssignmentSemanticAnalyzer assignmentAnalyzer)
{
    public void AnalyzeReturn(
        ReturnStatement statement,
        TypeRef returnType,
        TypeEnvironment typeEnvironment)
    {
        if (SemanticFacts.IsVoidType(returnType))
        {
            if (statement.Expression is not null)
            {
                diagnostics.Report(statement.Location, "Cannot return a value from function returning void.");
            }

            return;
        }

        AnalyzeReturnCore(
            statement,
            returnType,
            () => assignmentAnalyzer.CheckAssignmentCompatibility(statement.Location, returnType, statement.Expression, typeEnvironment, "return value"));
    }

    private void AnalyzeReturnCore(
        ReturnStatement statement,
        TypeRef returnType,
        Action checkAssignmentCompatibility)
    {
        if (statement.Expression is null)
        {
            diagnostics.Report(statement.Location, $"Function returning '{SemanticFacts.FormatTypeRef(returnType)}' must return a value.");
            return;
        }

        if (SemanticFacts.IsBareNull(statement.Expression) && !SemanticFacts.IsNullableType(returnType))
        {
            diagnostics.Report(statement.Location, $"Cannot return null from function returning non-pointer type '{SemanticFacts.FormatTypeRef(returnType)}'.");
        }

        checkAssignmentCompatibility();
    }
}
