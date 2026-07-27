using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lowering;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Testing;

internal sealed class TestProgramBuilder(DiagnosticBag diagnostics)
{
    public IReadOnlyList<ProgramNode> Build(
        IReadOnlyList<ProgramNode> programs,
        Func<ProgramNode, bool> collectTestsFromProgram,
        string? moduleName)
    {
        var selectedPrograms = programs
            .Where(collectTestsFromProgram)
            .ToList();
        var testCases = selectedPrograms
            .SelectMany(program => program.Tests.Select(test => (Program: program, Test: test)))
            .ToList();
        if (testCases.Count == 0)
        {
            var location = selectedPrograms.FirstOrDefault()?.Location
                ?? programs.FirstOrDefault()?.Location
                ?? Location.Synthetic("<tests>");
            diagnostics.Report(location, moduleName is null
                ? "No tests found."
                : $"No tests found in module '{moduleName}'.");
            return programs;
        }

        var generatedNames = new Dictionary<TestNode, string>();
        var selectedTestSet = testCases
            .Select(testCase => testCase.Test)
            .ToHashSet();
        var rewrittenPrograms = programs
            .Select(program =>
            {
                var testFunctions = program.Tests
                    .Where(selectedTestSet.Contains)
                    .Select((test, index) =>
                    {
                        var functionName = BuildTestFunctionName(program, test, index);
                        generatedNames[test] = functionName;
                        return new FunctionNode(
                            test.Location,
                            Name: functionName,
                            TypeParameters: [],
                            GenericConstraints: [],
                            Parameters:
                            [
                                new ParameterNode(
                                    test.Location,
                                    "runner",
                                    [],
                                    TypeNode: ResolvedTypeNode(
                                        test.Location,
                                        new TypeRef.Pointer(new TypeRef.Named("TestRunner", []))))
                            ],
                            Body: new TestAssertionRewriter().RewriteBody(test.Body),
                            Attributes: [],
                            ReturnTypeNode: ResolvedTypeNode(test.Location, TypeRef.Void),
                            OwnerTypeNode: null)
                        {
                            Visibility = DeclarationVisibility.Public,
                        };
                    })
                    .ToList();

                return program with
                {
                    Functions = program.Functions
                        .Where(function => function.OwnerTypeNode is not null || function.Name != "main")
                        .Concat(testFunctions)
                        .ToList(),
                };
            })
            .ToList();

        var rootLocation = Location.Synthetic("<tests>");
        var rootDeclarations = new List<TopLevelNode>();
        foreach (var importedModuleName in testCases
            .Select(testCase => testCase.Program.Module?.Name)
            .Where(importedModuleName => !string.IsNullOrWhiteSpace(importedModuleName))
            .Distinct(StringComparer.Ordinal))
        {
            rootDeclarations.Add(new ImportNode(
                rootLocation,
                importedModuleName!,
                Alias: null));
        }

        rootDeclarations.Add(new FunctionNode(
            rootLocation,
            Name: "main",
            TypeParameters: [],
            GenericConstraints: [],
            Parameters: [],
            Body: BuildTestMainBody(testCases, generatedNames),
            Attributes: [],
            ReturnTypeNode: ResolvedTypeNode(rootLocation, TypeRef.Int),
            OwnerTypeNode: null));

        rewrittenPrograms.Add(new ProgramNode(rootLocation, rootDeclarations));
        return rewrittenPrograms;
    }

    private static string BuildTestFunctionName(
        ProgramNode program,
        TestNode test,
        int index)
    {
        var moduleName = program.Module?.Name ?? "root";
        var text = "__cx_test_" + moduleName + "_" + test.Name + "_" + index;
        return new string(text
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
    }

    private static IReadOnlyList<StatementNode> BuildTestMainBody(
        IReadOnlyList<(ProgramNode Program, TestNode Test)> testCases,
        IReadOnlyDictionary<TestNode, string> generatedNames)
    {
        var location = Location.Synthetic("<tests>");
        var body = new List<StatementNode>
        {
            new LetStatement(
                location,
                IsConst: false,
                Name: "runner",
                Initializer: StaticCall(location, "TestRunner", "create", []),
                TypeNode: ResolvedTypeNode(
                    location,
                    new TypeRef.Named("TestRunner", []))),
        };

        foreach (var (_, test) in testCases)
        {
            body.Add(new CStatement(
                test.Location,
                RunnerCall(
                    test.Location,
                    "begin",
                    [
                        LiteralExpressionNode.String(
                            test.Location,
                            $"\"{EscapeStringLiteral(test.Name)}\"")
                    ])));
            body.Add(new CStatement(
                test.Location,
                new CallExpressionNode(
                    test.Location,
                    new NameExpressionNode(test.Location, generatedNames[test]),
                    [AddressOf(test.Location, "runner")])));
            body.Add(new CStatement(
                test.Location,
                RunnerCall(test.Location, "end", [])));
        }

        body.Add(new ReturnStatement(
            location,
            RunnerCall(location, "result", [])));
        return body;
    }

    private static CallExpressionNode RunnerCall(
        Location location,
        string methodName,
        IReadOnlyList<ExpressionNode> arguments) =>
        new(
            location,
            new MemberExpressionNode(
                location,
                new NameExpressionNode(location, "runner"),
                methodName),
            arguments);

    private static CallExpressionNode StaticCall(
        Location location,
        string typeName,
        string methodName,
        IReadOnlyList<ExpressionNode> arguments) =>
        new(
            location,
            new MemberExpressionNode(
                location,
                new NameExpressionNode(location, typeName),
                methodName),
            arguments);

    private static UnaryExpressionNode AddressOf(
        Location location,
        string name) =>
        new(
            location,
            UnaryOperator.AddressOf,
            new NameExpressionNode(location, name));

    private static TypeNode ResolvedTypeNode(Location location, TypeRef type)
    {
        var typeNode = type.ToTypeNode(location);
        typeNode.Semantic.Type = type;
        return typeNode;
    }

    private static string EscapeStringLiteral(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private sealed class TestAssertionRewriter : AstRewriter
    {
        private static readonly IReadOnlyDictionary<string, TestHelper> Helpers =
            new Dictionary<string, TestHelper>(StringComparer.Ordinal)
            {
                ["expect"] = new("expect", 1, "expect failed"),
                ["expect_true"] = new("expect_true", 1, "expect_true failed"),
                ["expect_false"] = new("expect_false", 1, "expect_false failed"),
                ["expect_eq_bool"] = new("expect_bool", 2, "expect_eq_bool failed"),
                ["expect_eq_int"] = new("expect_int", 2, "expect_eq_int failed"),
                ["expect_eq_u64"] = new("expect_u64", 2, "expect_eq_u64 failed"),
                ["expect_eq_usize"] = new("expect_usize", 2, "expect_eq_usize failed"),
                ["expect_eq_double"] = new("expect_double", 2, "expect_eq_double failed"),
                ["expect_near_double"] = new("expect_near_double", 3, "expect_near_double failed"),
                ["expect_eq_string"] = new("expect_string", 2, "expect_eq_string failed"),
                ["expect_eq_string_view"] = new("expect_string_view", 2, "expect_eq_string_view failed"),
                ["expect_null"] = new("expect_null", 1, "expect_null failed"),
                ["expect_not_null"] = new("expect_not_null", 1, "expect_not_null failed"),
            };

        public IReadOnlyList<StatementNode> RewriteBody(
            IReadOnlyList<StatementNode> body) =>
            RewriteStatements(body);

        protected override ExpressionNode RewriteCallExpression(
            CallExpressionNode call)
        {
            var rewritten = (CallExpressionNode)base.RewriteCallExpression(call);
            if (rewritten.Callee is not NameExpressionNode name
                || !Helpers.TryGetValue(name.Name, out var helper)
                || rewritten.Arguments.Count != helper.ArgumentCount)
            {
                return rewritten;
            }

            return rewritten with
            {
                Callee = new MemberExpressionNode(
                    name.Location,
                    new NameExpressionNode(name.Location, "runner"),
                    helper.MethodName),
                Arguments = rewritten.Arguments
                    .Append(LiteralExpressionNode.String(
                        rewritten.Location,
                        $"\"{helper.FailureMessage}\""))
                    .ToList(),
            };
        }

        private sealed record TestHelper(
            string MethodName,
            int ArgumentCount,
            string FailureMessage);
    }
}
