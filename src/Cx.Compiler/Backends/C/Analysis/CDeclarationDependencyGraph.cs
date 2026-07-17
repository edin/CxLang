using Cx.Compiler.C;

namespace Cx.Compiler;

internal enum CDeclarationKind
{
    Function,
    Type,
    Value,
}

internal sealed record CDeclarationId(CDeclarationKind Kind, string Name);

internal sealed record CDeclarationDependencyGraph(
    IReadOnlyDictionary<CDeclarationId, IReadOnlySet<CDeclarationId>> Dependencies,
    IReadOnlyDictionary<CTranslationUnitItem, IReadOnlySet<CDeclarationId>> ProvidedDeclarations)
{
    public IReadOnlySet<CDeclarationId> Declarations => Dependencies.Keys.ToHashSet();
}

internal static class CDeclarationDependencyAnalyzer
{
    public static CDeclarationDependencyGraph Analyze(CTranslationUnit unit)
    {
        var providedByItem = new Dictionary<CTranslationUnitItem, IReadOnlySet<CDeclarationId>>(
            ReferenceEqualityComparer.Instance);
        foreach (var item in unit.Items)
        {
            providedByItem[item] = ProvidedBy(item).ToHashSet();
        }
        var declarations = providedByItem.Values
            .SelectMany(ids => ids)
            .ToHashSet();
        var dependencies = declarations.ToDictionary(
            declaration => declaration,
            _ => new HashSet<CDeclarationId>());

        foreach (var item in unit.Items)
        {
            var itemDependencies = DependenciesOf(item)
                .Where(declarations.Contains)
                .ToHashSet();
            foreach (var provided in providedByItem[item])
            {
                dependencies[provided].UnionWith(itemDependencies);
            }
        }

        return new CDeclarationDependencyGraph(
            dependencies.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<CDeclarationId>)pair.Value),
            providedByItem);
    }

    private static IEnumerable<CDeclarationId> ProvidedBy(CTranslationUnitItem item) => item switch
    {
        CEnumDeclaration declaration =>
            [Type(declaration.Name), .. declaration.Members.Select(member => Value(member.Name))],
        CStructDeclaration declaration => [Type(declaration.Name)],
        CTaggedUnionDeclaration declaration =>
            [
                Type(declaration.Name),
                Type(declaration.Name + "Tag"),
                .. declaration.Variants.Select(variant => Value($"{declaration.Name}_Tag_{variant.Name}")),
            ],
        CTypeAliasDeclaration declaration => [Type(declaration.Name)],
        CFunctionDeclaration declaration => [Function(declaration.Signature.Name)],
        CFunctionDefinition definition => [Function(definition.Signature.Name)],
        CGlobalDeclaration declaration => [Value(declaration.Declaration.Name)],
        CExternGlobalDeclaration declaration => [Value(declaration.Declaration.Name)],
        _ => [],
    };

    private static IEnumerable<CDeclarationId> DependenciesOf(CTranslationUnitItem item) => item switch
    {
        CStructDeclaration declaration => declaration.Fields.SelectMany(field => TypeDependencies(field.Type)),
        CTaggedUnionDeclaration declaration => declaration.Variants.SelectMany(VariantDependencies),
        CTypeAliasDeclaration declaration => TypeDependencies(declaration.TargetType),
        CFunctionDeclaration declaration => SignatureDependencies(declaration.Signature),
        CFunctionDefinition definition => SignatureDependencies(definition.Signature)
            .Concat(definition.Body.SelectMany(StatementDependencies)),
        CGlobalDeclaration declaration => TypeDependencies(declaration.Declaration.Type)
            .Concat(ExpressionDependencies(declaration.Initializer)),
        CExternGlobalDeclaration declaration => TypeDependencies(declaration.Declaration.Type),
        _ => [],
    };

    private static IEnumerable<CDeclarationId> VariantDependencies(CTaggedUnionVariantDeclaration variant) =>
        TypeDependencies(variant.Type).Concat(TypeDependencies(variant.FieldDeclaration.Type));

    private static IEnumerable<CDeclarationId> SignatureDependencies(CFunctionSignature signature) =>
        TypeDependencies(signature.ReturnType)
            .Concat(signature.Parameters.SelectMany(parameter => TypeDependencies(parameter.Type)));

    private static IEnumerable<CDeclarationId> StatementDependencies(CStatementNode statement) => statement switch
    {
        CBlockStatement block => block.Body.SelectMany(StatementDependencies),
        CLocalDeclarationStatement local => TypeDependencies(local.Declaration.Type)
            .Concat(ExpressionDependencies(local.Initializer)),
        CReturnStatement ret => ExpressionDependencies(ret.Expression),
        CExpressionStatement expression => ExpressionDependencies(expression.Expression),
        CIfStatement conditional => ExpressionDependencies(conditional.Condition)
            .Concat(conditional.ThenBody.SelectMany(StatementDependencies))
            .Concat(ElseDependencies(conditional.ElseClause)),
        CWhileStatement loop => ExpressionDependencies(loop.Condition)
            .Concat(loop.Body.SelectMany(StatementDependencies)),
        CForStatement loop => ForInitializerDependencies(loop.Initializer)
            .Concat(ExpressionDependencies(loop.Condition))
            .Concat(ExpressionDependencies(loop.Increment))
            .Concat(loop.Body.SelectMany(StatementDependencies)),
        CSwitchStatement selection => ExpressionDependencies(selection.Expression)
            .Concat(selection.Cases.SelectMany(@case =>
                ExpressionDependencies(@case.Pattern).Concat(@case.Body.SelectMany(StatementDependencies))))
            .Concat(selection.DefaultBody.SelectMany(StatementDependencies)),
        _ => [],
    };

    private static IEnumerable<CDeclarationId> ElseDependencies(CElseClause? clause) => clause switch
    {
        CElseIfClause elseIf => StatementDependencies(elseIf.IfStatement),
        CElseBlockClause elseBlock => elseBlock.Body.SelectMany(StatementDependencies),
        _ => [],
    };

    private static IEnumerable<CDeclarationId> ForInitializerDependencies(CForInitializerNode initializer) => initializer switch
    {
        CDeclarationForInitializer declaration => TypeDependencies(declaration.Declaration.Type)
            .Concat(ExpressionDependencies(declaration.Initializer)),
        CExpressionForInitializer expression => ExpressionDependencies(expression.Expression),
        _ => [],
    };

    private static IEnumerable<CDeclarationId> ExpressionDependencies(CExpression? expression) => expression switch
    {
        null or CLiteralExpression => [],
        CNameExpression name => [Function(name.Name), Value(name.Name)],
        CParenthesizedExpression parenthesized => ExpressionDependencies(parenthesized.Expression),
        CCastExpression cast => TypeDependencies(cast.TargetType).Concat(ExpressionDependencies(cast.Expression)),
        CUnaryExpression unary => ExpressionDependencies(unary.Operand),
        CPostfixExpression postfix => ExpressionDependencies(postfix.Operand),
        CSizeOfTypeExpression sizeOf => TypeDependencies(sizeOf.Type),
        CSizeOfExpression sizeOf => ExpressionDependencies(sizeOf.Expression),
        CBinaryExpression binary => ExpressionDependencies(binary.Left).Concat(ExpressionDependencies(binary.Right)),
        CConditionalExpression conditional => ExpressionDependencies(conditional.Condition)
            .Concat(ExpressionDependencies(conditional.WhenTrue))
            .Concat(ExpressionDependencies(conditional.WhenFalse)),
        CAssignmentExpression assignment => ExpressionDependencies(assignment.Target)
            .Concat(ExpressionDependencies(assignment.Value)),
        CMemberExpression member => ExpressionDependencies(member.Target),
        CIndexExpression index => ExpressionDependencies(index.Target).Concat(ExpressionDependencies(index.Index)),
        CCommaExpression comma => comma.Expressions.SelectMany(ExpressionDependencies),
        CInitializerExpression initializer => TypeDependencies(initializer.Type)
            .Concat(initializer.Fields.SelectMany(field => ExpressionDependencies(field.Value)))
            .Concat(initializer.Values.SelectMany(ExpressionDependencies)),
        CCallExpression call => new[] { Function(call.Function.Name) }
            .Concat(call.Arguments.SelectMany(ExpressionDependencies)),
        CExpressionCallExpression call => ExpressionDependencies(call.Function)
            .Concat(call.Arguments.SelectMany(ExpressionDependencies)),
        _ => [],
    };

    private static IEnumerable<CDeclarationId> TypeDependencies(CTypeRef? type) => type switch
    {
        null => [],
        CNamedTypeRef named => [Type(named.Name)],
        CStructTypeRef @struct => [Type(@struct.Name)],
        CPointerTypeRef pointer => TypeDependencies(pointer.Element),
        CConstTypeRef constant => TypeDependencies(constant.Element),
        CFixedArrayTypeRef array => TypeDependencies(array.Element),
        CFunctionTypeRef function => TypeDependencies(function.ReturnType)
            .Concat(function.Parameters.SelectMany(parameter => TypeDependencies(parameter.Type))),
        _ => [],
    };

    private static CDeclarationId Function(string name) => new(CDeclarationKind.Function, name);

    private static CDeclarationId Type(string name) => new(CDeclarationKind.Type, name);

    private static CDeclarationId Value(string name) => new(CDeclarationKind.Value, name);
}
