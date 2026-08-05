using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Syntax;

internal static class AstChildren
{
    public static IEnumerable<SyntaxNode> Get(SyntaxNode node) => node switch
    {
        ProgramNode program => program.Declarations,
        ModuleBlockNode module => module.Declarations,
        SyntaxBlockNode block => block.Items,
        CDeclareNode declaration => declaration.Members,
        SymbolImportNode import => import.Symbols,

        AttributeDeclarationNode attribute => Children(attribute.Fields),
        AttributeFieldNode field => Children(field.TypeNode),
        CompileTimeListTypeNode list => Children(list.ElementType),
        AttributeApplicationNode attribute => Children(attribute.Arguments),
        AttributeArgumentNode argument => Children(argument.Value),

        TypeAliasNode alias => Children(alias.Attributes, alias.TargetTypeNode),
        EnumNode enumNode => Children(
            enumNode.Members,
            enumNode.Attributes,
            enumNode.DataFields),
        EnumMemberNode member => Children(
            member.Attributes,
            member.DataValues),
        EnumDataFieldNode field => Children(
            field.TypeNode,
            field.DefaultValue),
        EnumDataValueNode value => Children(value.Value),
        InterfaceNode interfaceNode => Children(
            interfaceNode.Methods,
            interfaceNode.Attributes),
        InterfaceMethodNode method => Children(
            method.Parameters,
            method.ReturnTypeNode),
        StructNode structNode => Children(
            structNode.GenericConstraints,
            structNode.Requirements,
            structNode.Members,
            structNode.Attributes),
        GenericConstraintNode constraint => Children(constraint.Requirements),
        StructRequirementNode requirement => Children(
            requirement.TypeArgumentNodes),
        StructFieldNode field => Children(field.Attributes, field.TypeNode),
        RequirementNode requirement => Children(
            requirement.GenericConstraints,
            requirement.Members),
        RequirementFieldNode field => Children(field.TypeNode),
        RequirementFunctionNode function => Children(
            function.Parameters,
            function.ReturnTypeNode),
        TaggedUnionNode union => Children(
            union.Variants,
            union.Methods,
            union.Attributes),
        TaggedUnionVariantNode variant => Children(
            variant.Attributes,
            variant.TypeNode),
        ExtensionNode extension => Children(
            extension.GenericConstraints,
            extension.Members,
            extension.Attributes,
            extension.TargetTypeNode),
        TypeAdapterNode adapter => Children(
            adapter.Members,
            adapter.Attributes,
            adapter.BaseTypeNode),
        ExposeMethodNode exposed => Children(exposed.ReturnTypeNode),
        GlobalVariableNode global => Children(
            global.Initializer,
            global.Attributes,
            global.TypeNode),
        CompileTimeConstantNode constant => Children(
            constant.TypeNode,
            constant.Initializer,
            constant.Attributes),
        FunctionNode function => Children(
            function.ComputedName,
            function.ComputedParameters,
            function.Body,
            function.GenericConstraints,
            function.Parameters,
            function.Attributes,
            function.ReturnTypeNode,
            function.OwnerTypeNode,
            function.TypeArgumentNodes),
        ExternFunctionNode function => Children(
            function.Parameters,
            function.Attributes,
            function.ReturnTypeNode),
        ParameterNode parameter => Children(
            parameter.Attributes,
            parameter.TypeNode),
        TestNode test => Children(test.Body, test.Attributes),

        MacroDeclarationNode macro => Children(
            macro.Parameters,
            macro.Template,
            macro.ProvidedRequirements),
        MacroTemplateBlockNode template => Children(
            template.Statements,
            template.Declarations),
        MacroArgumentNode argument => Children(
            argument.ExpressionCandidate,
            argument.TypeCandidate),
        MacroInvocationDeclarationNode invocation => Children(
            invocation.Arguments),
        MacroInvocationStatementNode invocation => Children(
            invocation.Arguments),
        MacroProvidedRequirementNode requirement => Children(
            requirement.Requirement),

        CompileTimeScriptDeclarationNode script => Children(script.Statement),
        CompileTimeIfTopLevelNode conditional => Children(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock),
        CompileTimeForeachTopLevelNode loop => Children(
            loop.IterableExpression,
            loop.Body),
        CompileTimeIfDeclarationNode conditional => Children(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock),
        CompileTimeForeachDeclarationNode loop => Children(
            loop.IterableExpression,
            loop.Body),
        CompileTimeLetStatementNode let => Children(let.Initializer),
        CompileTimeIfStatementNode conditional => Children(
            conditional.Condition,
            conditional.ThenBlock,
            conditional.ElseBlock),
        CompileTimeForeachStatementNode loop => Children(
            loop.IterableExpression,
            loop.Body),

        LetStatement let => Children(let.Initializer, let.TypeNode),
        UsingStatement usingStatement => Children(
            usingStatement.Initializer,
            usingStatement.TypeNode),
        ReturnStatement returnStatement => Children(returnStatement.Expression),
        IfStatement conditional => Children(
            conditional.Condition,
            conditional.ThenBody,
            conditional.ElseBranch),
        ElseBlockStatement elseBlock => Children(elseBlock.Body),
        WhileStatement loop => Children(loop.Condition, loop.Body),
        ForStatement loop => Children(
            loop.CachedRangeEndInitializer,
            loop.CounterInitializer,
            loop.Initializer,
            loop.Condition,
            loop.Increment,
            loop.CounterIncrement,
            loop.Body),
        ForDeclarationInitializerNode declaration => Children(
            declaration.Initializer,
            declaration.TypeNode),
        ForExpressionInitializerNode expression => Children(
            expression.Expression),
        ForeachStatement loop => Children(
            loop.IndexBinding,
            loop.KeyBinding,
            loop.ValueBinding,
            loop.IterableExpression,
            loop.Body),
        ForeachBinding binding => Children(binding.TypeNode),
        SwitchStatement switchStatement => Children(
            switchStatement.Expression,
            switchStatement.Cases,
            switchStatement.DefaultBody),
        SwitchCaseNode switchCase => Children(
            switchCase.Pattern,
            switchCase.Body),
        MatchStatement match => Children(match.Expression, match.Arms),
        MatchArmNode arm => Children(arm.Body),
        CStatement statement => Children(statement.Expression),

        PlaceholderExpressionNode placeholder => Children(
            placeholder.Expression),
        ParenthesizedExpressionNode parenthesized => Children(
            parenthesized.Expression),
        CastExpressionNode cast => Children(
            cast.Expression,
            cast.TargetTypeNode),
        UnaryExpressionNode unary => Children(unary.Operand),
        PostfixExpressionNode postfix => Children(postfix.Operand),
        SizeOfExpressionNode sizeOf => SizeOfChildren(sizeOf.Operand),
        BinaryExpressionNode binary => Children(binary.Left, binary.Right),
        ConditionalExpressionNode conditional => Children(
            conditional.Condition,
            conditional.WhenTrue,
            conditional.WhenFalse),
        TryExpressionNode attempt => Children(
            attempt.Expression,
            attempt.Fallback),
        ScalarRangeExpressionNode range => Children(range.Start, range.End),
        ListExpressionNode list => Children(list.Elements),
        TypeLiteralExpressionNode typeLiteral => Children(typeLiteral.TypeNode),
        InitializerExpressionNode initializer => Children(
            initializer.Fields.Select(field => field.Value),
            initializer.Values,
            initializer.TypeNameNode),
        FunctionExpressionNode function => Children(
            function.ExpressionBody,
            function.BlockBody,
            function.Parameters,
            function.ReturnTypeNode),
        AssignmentExpressionNode assignment => Children(
            assignment.Target,
            assignment.Value),
        CallExpressionNode call => Children(call.Callee, call.Arguments),
        GenericCallExpressionNode call => Children(
            call.Callee,
            call.Arguments,
            call.TypeArgumentNodes),
        MemberExpressionNode member => Children(member.Target),
        IncompleteMemberExpressionNode member => Children(member.Target),
        ComputedMemberExpressionNode member => Children(
            member.Target,
            member.MemberName),
        IndexExpressionNode index => Children(index.Target, index.Index),

        TypeNode type => TypeChildren(type.Syntax),
        ModuleDeclarationNode
            or ImportNode
            or ImportedSymbolNode
            or IncludeNode
            or CLinkNode
            or MacroParameterNode
            or CompileTimeScalarTypeNode
            or CompileTimeErrorTypeNode
            or BreakStatement
            or ContinueStatement
            or ErrorExpressionNode
            or LiteralExpressionNode
            or NameExpressionNode => [],
        _ => throw Unsupported(node),
    };

    private static NotSupportedException Unsupported(SyntaxNode node) =>
        new(
            $"AST child enumeration is not registered for syntax node "
            + $"'{node.GetType().Name}'.");

    private static IEnumerable<SyntaxNode> SizeOfChildren(
        SizeOfOperandNode operand) =>
        operand switch
        {
            SizeOfTypeOperandNode type => Children(type.TypeNode),
            SizeOfExpressionOperandNode expression =>
                Children(expression.Expression),
            SizeOfUnresolvedOperandNode unresolved =>
                Children(unresolved.ExpressionCandidate),
            _ => [],
        };

    private static IEnumerable<SyntaxNode> TypeChildren(
        TypeSyntaxNode syntax) =>
        syntax switch
        {
            ComputedTypeSyntaxNode computed => Children(computed.Expression),
            GenericTypeSyntaxNode generic => generic.Arguments
                .Prepend(generic.Target)
                .SelectMany(TypeChildren),
            PointerTypeSyntaxNode pointer => TypeChildren(pointer.Element),
            ConstTypeSyntaxNode constType => TypeChildren(constType.Element),
            NullableTypeSyntaxNode nullable => TypeChildren(nullable.Element),
            FixedArrayTypeSyntaxNode array => TypeChildren(array.Element),
            FunctionTypeSyntaxNode function => function.Parameters
                .Append(function.ReturnType)
                .SelectMany(TypeChildren),
            _ => [],
        };

    private static IEnumerable<SyntaxNode> Children(
        params object?[] values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case SyntaxNode node:
                    yield return node;
                    break;
                case IEnumerable<SyntaxNode> nodes:
                    foreach (var child in nodes)
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }
}
