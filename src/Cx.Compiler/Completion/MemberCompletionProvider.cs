using Cx.Compiler.Diagnostics;
using Cx.Compiler.Lexer;
using Cx.Compiler.Parser;
using Cx.Compiler.Semantic;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Completion;

internal sealed class MemberCompletionProvider(
    Func<IReadOnlyList<SourceFile>, ProgramNode?> compileForAnalysis)
{
    public IReadOnlyList<MemberCompletion> Get(
        IEnumerable<SourceFile> sources,
        string path,
        int position)
    {
        var sourceFiles = sources.ToList();
        var targetIndex = sourceFiles.FindIndex(source =>
            SourcePathsEqual(source.Path, path));
        if (targetIndex < 0)
        {
            return [];
        }

        var target = sourceFiles[targetIndex];
        if (position <= 0
            || position > target.Text.Length
            || target.Text[position - 1] != '.')
        {
            return [];
        }

        if (TryGetDataEnumDefaultMemberCompletions(
            target,
            position,
            out var contextualCompletions))
        {
            return contextualCompletions;
        }

        PrepareIncompleteExpression(sourceFiles, targetIndex, target, position);
        var program = compileForAnalysis(sourceFiles);
        if (program is null)
        {
            return [];
        }

        var hole = ExecutableAstTraversal
            .DescendantsAndSelf<IncompleteMemberExpressionNode>(program)
            .LastOrDefault(member =>
                SourcePathsEqual(member.DotSpan.File.Path, path)
                && member.DotSpan.Position == position - 1);
        if (hole is null)
        {
            return [];
        }

        if (hole.Target.Semantic.Type is { } receiverType)
        {
            return CollectMemberCompletions(
                program,
                receiverType,
                hole.Prefix);
        }

        if (hole.Target is NameExpressionNode { Name: "member" }
            && IsDataEnumDefaultExpression(program, hole))
        {
            return CollectMemberCompletions(
                program,
                DataEnumMemberContextFacts.ContextType,
                hole.Prefix);
        }

        return CollectStaticMemberCompletions(
            program,
            hole.Target,
            hole.Prefix);
    }

    private static void PrepareIncompleteExpression(
        IList<SourceFile> sources,
        int targetIndex,
        SourceFile target,
        int position)
    {
        var nextNonWhitespace = position;
        while (nextNonWhitespace < target.Text.Length
            && char.IsWhiteSpace(target.Text[nextNonWhitespace]))
        {
            nextNonWhitespace++;
        }

        var isDelimitedExpression = nextNonWhitespace < target.Text.Length
            && target.Text[nextNonWhitespace] is ',' or ')';
        if (!isDelimitedExpression
            && (position == target.Text.Length || target.Text[position] != ';'))
        {
            sources[targetIndex] = target with
            {
                Text = target.Text.Insert(position, ";"),
            };
        }
    }

    private static bool TryGetDataEnumDefaultMemberCompletions(
        SourceFile source,
        int position,
        out IReadOnlyList<MemberCompletion> completions)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer.Lexer(source, diagnostics).Tokenize();
        var program = new Parser.Parser(diagnostics).Parse(source, tokens);
        var hole = ExecutableAstTraversal
            .DescendantsAndSelf<IncompleteMemberExpressionNode>(program)
            .LastOrDefault(member => member.DotSpan.Position == position - 1);

        if (hole?.Target is NameExpressionNode { Name: "member" }
            && IsDataEnumDefaultExpression(program, hole))
        {
            completions = CollectMemberCompletions(
                program,
                DataEnumMemberContextFacts.ContextType,
                hole.Prefix);
            return true;
        }

        completions = [];
        return false;
    }

    private static bool IsDataEnumDefaultExpression(
        ProgramNode program,
        IncompleteMemberExpressionNode hole) =>
        program.Enums
            .Where(enumNode => enumNode.IsDataEnum)
            .SelectMany(enumNode => enumNode.DataFields ?? [])
            .Where(field => field.DefaultValue is not null)
            .Any(field => ExecutableAstTraversal
                .DescendantsAndSelf<IncompleteMemberExpressionNode>(
                    field.DefaultValue!)
                .Any(expression => ReferenceEquals(expression, hole)));

    private static IReadOnlyList<MemberCompletion> CollectStaticMemberCompletions(
        ProgramNode program,
        ExpressionNode target,
        string prefix)
    {
        var targetName = ExpressionNameFacts.GetQualifiedName(target);
        if (targetName is null)
        {
            return [];
        }

        var enumNode = program.Enums.FirstOrDefault(candidate =>
            candidate.Name == targetName);
        if (enumNode is null)
        {
            return [];
        }

        return enumNode.Members
            .Where(member => member.Name.StartsWith(
                prefix,
                StringComparison.Ordinal))
            .Select(member => new MemberCompletion(
                member.Name,
                MemberCompletionKind.EnumMember,
                enumNode.Name))
            .OrderBy(completion =>
                completion.Label,
                StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<MemberCompletion> CollectMemberCompletions(
        ProgramNode program,
        TypeRef receiverType,
        string prefix)
    {
        if (DataEnumMemberContextFacts.IsContextType(receiverType))
        {
            return new[]
            {
                new MemberCompletion(
                    "name",
                    MemberCompletionKind.Field,
                    "const char*"),
                new MemberCompletion(
                    "index",
                    MemberCompletionKind.Field,
                    "int"),
            }
            .Where(completion => completion.Label.StartsWith(
                prefix,
                StringComparison.Ordinal))
            .OrderBy(completion =>
                completion.Label,
                StringComparer.Ordinal)
            .ToList();
        }

        var typeName = TypeRefFacts.GetBaseName(receiverType);
        if (typeName is null)
        {
            return [];
        }

        var completions = new List<MemberCompletion>();
        var structNode = program.Structs.FirstOrDefault(node =>
            node.Name == typeName);
        if (structNode is not null)
        {
            completions.AddRange(structNode.Fields.Select(field =>
                FieldCompletion(field.Name, field.TypeNode)));
        }

        var dataEnum = program.Enums.FirstOrDefault(node =>
            node.IsDataEnum && node.Name == typeName);
        if (dataEnum?.DataFields is not null)
        {
            completions.AddRange(dataEnum.DataFields.Select(field =>
                FieldCompletion(field.Name, field.TypeNode)));
        }

        var union = program.TaggedUnions.FirstOrDefault(node =>
            node.Name == typeName);
        if (union is not null)
        {
            completions.AddRange(union.Variants.Select(variant =>
                FieldCompletion(variant.Name, variant.TypeNode)));
        }

        completions.AddRange(new TypeSystem(program)
            .GetMethods(receiverType)
            .Where(method => !method.Declaration.IsStatic)
            .Select(method => MethodCompletion(method.Declaration)));

        return completions
            .Where(completion => completion.Label.StartsWith(
                prefix,
                StringComparison.Ordinal))
            .DistinctBy(completion => (completion.Label, completion.Kind))
            .OrderBy(completion => completion.Kind)
            .ThenBy(
                completion => completion.Label,
                StringComparer.Ordinal)
            .ToList();
    }

    private static MemberCompletion FieldCompletion(
        string name,
        TypeNode? typeNode) =>
        new(
            name,
            MemberCompletionKind.Field,
            typeNode?.ToSourceText() ?? "<unknown>");

    private static MemberCompletion MethodCompletion(FunctionNode method)
    {
        var parameters = method.Parameters
            .Where(parameter => parameter.Name != "self")
            .Select(parameter =>
                $"{parameter.Name}: {parameter.TypeNode?.ToSourceText() ?? "<unknown>"}");
        var detail =
            $"fn {method.Name}({string.Join(", ", parameters)}) -> " +
            $"{method.ReturnTypeNode?.ToSourceText() ?? "void"}";
        return new MemberCompletion(
            method.Name,
            MemberCompletionKind.Method,
            detail);
    }

    private static bool SourcePathsEqual(string left, string right)
    {
        if (left.StartsWith('<') || right.StartsWith('<'))
        {
            return string.Equals(
                left,
                right,
                StringComparison.Ordinal);
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
