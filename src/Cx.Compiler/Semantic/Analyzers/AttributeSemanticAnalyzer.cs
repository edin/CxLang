using Cx.Compiler.CompileTime;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Analyzers;

internal sealed class AttributeSemanticAnalyzer(DiagnosticBag diagnostics)
{
    private CompileTimeExpressionEvaluator? _evaluator;

    public void Analyze(ProgramNode program)
    {
        _evaluator = new CompileTimeExpressionEvaluator(
            diagnostics,
            reflection: new ProgramCompileTimeReflection(program));
        var declarations = program.AttributeDeclarations
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        foreach (var group in program.AttributeDeclarations.GroupBy(attribute => attribute.Name, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                diagnostics.Report(group.Last().Location, $"Attribute '{group.Key}' is declared more than once.");
            }
        }

        foreach (var typeAlias in program.TypeAliases)
        {
            AnalyzeAttributeApplications(typeAlias.Attributes, "type_alias", declarations);
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            AnalyzeAttributeApplications(externFunction.Attributes, "extern", declarations);
            foreach (var parameter in externFunction.Parameters)
            {
                AnalyzeAttributeApplications(parameter.Attributes, "parameter", declarations);
            }
        }

        foreach (var global in program.GlobalVariables)
        {
            AnalyzeAttributeApplications(global.Attributes, "global", declarations);
        }

        foreach (var enumNode in program.Enums)
        {
            AnalyzeAttributeApplications(enumNode.Attributes, "enum", declarations);
            foreach (var member in enumNode.Members)
            {
                AnalyzeAttributeApplications(member.Attributes, "variant", declarations);
            }
        }

        foreach (var structNode in program.Structs)
        {
            AnalyzeAttributeApplications(structNode.Attributes, "struct", declarations);
            foreach (var field in structNode.Fields)
            {
                AnalyzeAttributeApplications(field.Attributes, "field", declarations);
            }
        }

        foreach (var taggedUnion in program.TaggedUnions)
        {
            AnalyzeAttributeApplications(taggedUnion.Attributes, "union", declarations);
            foreach (var variant in taggedUnion.Variants)
            {
                AnalyzeAttributeApplications(variant.Attributes, "variant", declarations);
            }
        }

        foreach (var function in program.Functions)
        {
            AnalyzeFunctionAttributes(function, declarations);
        }
    }

    private void AnalyzeFunctionAttributes(
        FunctionNode function,
        IReadOnlyDictionary<string, AttributeDeclarationNode> declarations)
    {
        AnalyzeAttributeApplications(function.Attributes, "fn", declarations);
        foreach (var parameter in function.Parameters)
        {
            AnalyzeAttributeApplications(parameter.Attributes, "parameter", declarations);
        }
    }

    private void AnalyzeAttributeApplications(
        IReadOnlyList<AttributeApplicationNode> applications,
        string target,
        IReadOnlyDictionary<string, AttributeDeclarationNode> declarations)
    {
        foreach (var duplicate in applications
            .GroupBy(attribute => attribute.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Report(duplicate.Last().Location, $"Attribute '{duplicate.Key}' cannot be applied more than once.");
        }

        foreach (var application in applications)
        {
            if (!declarations.TryGetValue(application.Name, out var declaration))
            {
                diagnostics.Report(application.Location, $"Unknown attribute '{application.Name}'.");
                continue;
            }

            if (!declaration.Targets.Contains(target, StringComparer.Ordinal))
            {
                diagnostics.Report(application.Location, $"Attribute '{application.Name}' cannot be applied to {target}.");
            }

            if (application.Arguments.All(argument => argument.Name is null))
            {
                if (application.Arguments.Count != declaration.Fields.Count)
                {
                    diagnostics.Report(application.Location, $"Attribute '{application.Name}' expects {declaration.Fields.Count} argument(s).");
                }

                foreach (var pair in application.Arguments.Zip(declaration.Fields))
                {
                    ValidateArgumentValue(application, pair.First, pair.Second);
                }

                continue;
            }

            AnalyzeNamedAttributeArguments(application, declaration);
        }
    }

    private void AnalyzeNamedAttributeArguments(
        AttributeApplicationNode application,
        AttributeDeclarationNode declaration)
    {
        var assignedFields = new HashSet<string>(StringComparer.Ordinal);
        var positionalIndex = 0;

        foreach (var argument in application.Arguments)
        {
            AttributeFieldNode? field;
            if (argument.Name is null)
            {
                field = positionalIndex < declaration.Fields.Count
                    ? declaration.Fields[positionalIndex++]
                    : null;
                if (field is null)
                {
                    diagnostics.Report(application.Location, $"Attribute '{application.Name}' has too many positional arguments.");
                    continue;
                }
            }
            else
            {
                field = declaration.Fields.FirstOrDefault(candidate => candidate.Name == argument.Name);
                if (field is null)
                {
                    diagnostics.Report(argument.Location, $"Attribute '{application.Name}' has no field named '{argument.Name}'.");
                    continue;
                }
            }

            if (!assignedFields.Add(field.Name))
            {
                diagnostics.Report(argument.Location, $"Attribute '{application.Name}' field '{field.Name}' is specified more than once.");
                continue;
            }

            ValidateArgumentValue(application, argument, field);
        }

        foreach (var field in declaration.Fields.Where(field => !assignedFields.Contains(field.Name)))
        {
            diagnostics.Report(application.Location, $"Attribute '{application.Name}' requires argument '{field.Name}'.");
        }
    }

    private void ValidateArgumentValue(
        AttributeApplicationNode application,
        AttributeArgumentNode argument,
        AttributeFieldNode field)
    {
        var evaluator = _evaluator ?? throw new InvalidOperationException("Attribute evaluator is not initialized.");
        var value = evaluator.Evaluate(argument.Value, new CompileTimeEvaluationContext());
        if (value is null || field.TypeNode is CompileTimeErrorTypeNode)
        {
            return;
        }

        if (!Matches(field.TypeNode, value))
        {
            diagnostics.Report(
                argument.Location,
                $"Attribute '{application.Name}' argument '{field.Name}' expects metadata type '{field.TypeNode.ToSourceText()}', but received {CompileTimeValueFacts.Describe(value)}.");
        }
    }

    private static bool Matches(CompileTimeTypeNode type, CompileTimeValue value) =>
        (type, value) switch
        {
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.Boolean }, CompileTimeValue.Boolean) => true,
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.Integer }, CompileTimeValue.Integer) => true,
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.String }, CompileTimeValue.String) => true,
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.Name }, CompileTimeValue.Name) => true,
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.Type }, CompileTimeValue.Type) => true,
            (CompileTimeScalarTypeNode { Kind: CompileTimeScalarType.Syntax }, CompileTimeValue.Syntax) => true,
            (CompileTimeListTypeNode list, CompileTimeValue.List values) =>
                values.Values.All(value => Matches(list.ElementType, value)),
            _ => false,
        };
}
