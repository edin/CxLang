using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

internal abstract class AstRewriter
{
    private readonly List<TopLevelNode> _injectedTopLevelDeclarations = [];

    public virtual ProgramNode RewriteProgram(ProgramNode program)
    {
        _injectedTopLevelDeclarations.Clear();
        var declarations = program.Declarations
            .SelectMany(RewriteTopLevelNode)
            .ToList();
        AfterRewriteTopLevelDeclarations(program, declarations);
        declarations.AddRange(_injectedTopLevelDeclarations);

        return program with
        {
            Declarations = declarations,
        };
    }

    protected void InjectTopLevelDeclaration(TopLevelNode declaration) =>
        _injectedTopLevelDeclarations.Add(declaration);

    protected void InjectTopLevelDeclarations(IEnumerable<TopLevelNode> declarations) =>
        _injectedTopLevelDeclarations.AddRange(declarations);

    protected virtual void AfterRewriteTopLevelDeclarations(
        ProgramNode program,
        IReadOnlyList<TopLevelNode> rewrittenDeclarations)
    {
    }

    protected virtual IReadOnlyList<TopLevelNode> RewriteTopLevelNode(TopLevelNode node) =>
        node switch
        {
            AttributeDeclarationNode attribute => [RewriteAttributeDeclaration(attribute)],
            CDeclareNode cDeclare => [RewriteCDeclare(cDeclare)],
            TypeAliasNode alias => [RewriteTypeAlias(alias)],
            RequirementNode requirement => [RewriteRequirement(requirement)],
            EnumNode enumNode => [RewriteEnum(enumNode)],
            InterfaceNode interfaceNode => [RewriteInterface(interfaceNode)],
            StructNode structNode => [RewriteStruct(structNode)],
            TypeAdapterNode adapter => [RewriteTypeAdapter(adapter)],
            ExtensionNode extension => [RewriteExtension(extension)],
            TaggedUnionNode union => [RewriteTaggedUnion(union)],
            GlobalVariableNode global => [RewriteGlobalVariable(global)],
            FunctionNode function => [RewriteFunction(function)],
            CompileTimeScriptDeclarationNode script => RewriteCompileTimeScriptDeclaration(script),
            CompileTimeIfTopLevelNode conditional => [RewriteCompileTimeIfTopLevel(conditional)],
            CompileTimeForeachTopLevelNode foreachNode => [RewriteCompileTimeForeachTopLevel(foreachNode)],
            MacroDeclarationNode macro => [RewriteMacroDeclaration(macro)],
            MacroInvocationDeclarationNode invocation => [RewriteMacroInvocationDeclaration(invocation)],
            TestNode test => [RewriteTest(test)],
            ExternFunctionNode externFunction => [RewriteExternFunction(externFunction)],
            _ => [node],
        };

    protected virtual IReadOnlyList<TopLevelNode> RewriteCompileTimeScriptDeclaration(
        CompileTimeScriptDeclarationNode script) =>
        RewriteStatement(script.Statement)
            .Select(statement => (TopLevelNode)SyntaxNode.CloneMetadata(
                script,
                script with { Statement = statement }))
            .ToList();

    protected virtual CompileTimeIfTopLevelNode RewriteCompileTimeIfTopLevel(
        CompileTimeIfTopLevelNode conditional) =>
        conditional with
        {
            Condition = RewriteRequiredExpression(conditional.Condition),
            ThenDeclarations = conditional.ThenDeclarations.SelectMany(RewriteTopLevelNode).ToList(),
            ElseDeclarations = conditional.ElseDeclarations.SelectMany(RewriteTopLevelNode).ToList(),
        };

    protected virtual CompileTimeForeachTopLevelNode RewriteCompileTimeForeachTopLevel(
        CompileTimeForeachTopLevelNode foreachNode) =>
        foreachNode with
        {
            IterableExpression = RewriteRequiredExpression(foreachNode.IterableExpression),
            Declarations = foreachNode.Declarations.SelectMany(RewriteTopLevelNode).ToList(),
        };

    protected virtual CDeclareNode RewriteCDeclare(CDeclareNode cDeclare) =>
        cDeclare with { Members = cDeclare.Members.Select(RewriteCDeclareMember).ToList() };

    protected virtual SyntaxNode RewriteCDeclareMember(SyntaxNode member) =>
        member switch
        {
            CompileTimeIfDeclarationNode conditional => conditional with
            {
                Condition = RewriteRequiredExpression(conditional.Condition),
                ThenMembers = conditional.ThenMembers.Select(RewriteCDeclareMember).ToList(),
                ElseMembers = conditional.ElseMembers.Select(RewriteCDeclareMember).ToList(),
            },
            CompileTimeForeachDeclarationNode foreachNode => foreachNode with
            {
                IterableExpression = RewriteRequiredExpression(foreachNode.IterableExpression),
                Members = foreachNode.Members.Select(RewriteCDeclareMember).ToList(),
            },
            _ => member,
        };

    protected virtual MacroDeclarationNode RewriteMacroDeclaration(MacroDeclarationNode macro) =>
        macro with
        {
            ProvidedRequirements = macro.ProvidedRequirementNodes.Select(provided => provided with
            {
                Requirement = RewriteStructRequirement(provided.Requirement),
            }).ToList(),
            Template = macro.Template with
            {
                Statements = RewriteStatements(macro.Template.Statements),
                Declarations = macro.Template.DeclarationNodes.SelectMany(RewriteTopLevelNode).ToList(),
            },
        };

    protected virtual MacroInvocationDeclarationNode RewriteMacroInvocationDeclaration(
        MacroInvocationDeclarationNode invocation) =>
        invocation with { Arguments = invocation.Arguments.Select(RewriteMacroArgument).ToList() };

    protected virtual MacroArgumentNode RewriteMacroArgument(MacroArgumentNode argument) =>
        argument with
        {
            ExpressionCandidate = RewriteExpression(argument.ExpressionCandidate),
            TypeCandidate = RewriteType(argument.TypeCandidate),
        };

    protected virtual FunctionNode RewriteFunction(FunctionNode function) =>
        function with
        {
            ComputedName = function.ComputedName is null
                ? null
                : function.ComputedName with
                {
                    Expression = RewriteRequiredExpression(function.ComputedName.Expression),
                },
            ComputedParameters = function.ComputedParameters is null
                ? null
                : function.ComputedParameters with
                {
                    Expression = RewriteRequiredExpression(function.ComputedParameters.Expression),
                },
            GenericConstraints = RewriteGenericConstraints(function.GenericConstraints),
            Parameters = RewriteParameters(function.Parameters),
            Body = RewriteStatements(function.Body),
            Attributes = RewriteAttributeApplications(function.Attributes),
            ReturnTypeNode = RewriteType(function.ReturnTypeNode),
            OwnerTypeNode = RewriteType(function.OwnerTypeNode),
            TypeArgumentNodes = RewriteTypes(function.TypeArgumentNodes),
        };

    protected virtual ExternFunctionNode RewriteExternFunction(ExternFunctionNode function) =>
        function with
        {
            Parameters = RewriteParameters(function.Parameters),
            Attributes = RewriteAttributeApplications(function.Attributes),
            ReturnTypeNode = RewriteType(function.ReturnTypeNode),
        };

    protected virtual GlobalVariableNode RewriteGlobalVariable(GlobalVariableNode global) =>
        global with
        {
            Initializer = RewriteExpression(global.Initializer),
            Attributes = RewriteAttributeApplications(global.Attributes),
            TypeNode = RewriteType(global.TypeNode),
        };

    protected virtual StructNode RewriteStruct(StructNode structNode) =>
        structNode with
        {
            GenericConstraints = RewriteGenericConstraints(structNode.GenericConstraints),
            Requirements = structNode.Requirements.Select(RewriteStructRequirement).ToList(),
            Fields = structNode.Fields.Select(RewriteStructField).ToList(),
            Methods = structNode.Methods.Select(RewriteFunction).ToList(),
            MacroInvocations = structNode.MacroInvocationNodes
                .Select(RewriteMacroInvocationDeclaration)
                .ToList(),
            Attributes = RewriteAttributeApplications(structNode.Attributes),
        };

    protected virtual ExtensionNode RewriteExtension(ExtensionNode extension) =>
        extension with
        {
            GenericConstraints = RewriteGenericConstraints(extension.GenericConstraints),
            Methods = extension.Methods.Select(RewriteFunction).ToList(),
            Attributes = RewriteAttributeApplications(extension.Attributes),
            TargetTypeNode = RewriteType(extension.TargetTypeNode),
        };

    protected virtual TypeAdapterNode RewriteTypeAdapter(TypeAdapterNode adapter) =>
        adapter with
        {
            ExposedMethods = adapter.ExposedMethods.Select(RewriteExposeMethod).ToList(),
            Methods = adapter.Methods.Select(RewriteFunction).ToList(),
            Attributes = RewriteAttributeApplications(adapter.Attributes),
            BaseTypeNode = RewriteType(adapter.BaseTypeNode),
        };

    protected virtual TaggedUnionNode RewriteTaggedUnion(TaggedUnionNode union) =>
        union with
        {
            Variants = union.Variants.Select(RewriteTaggedUnionVariant).ToList(),
            Methods = union.Methods.Select(RewriteFunction).ToList(),
            Attributes = RewriteAttributeApplications(union.Attributes),
        };

    protected virtual InterfaceNode RewriteInterface(InterfaceNode interfaceNode) =>
        interfaceNode with
        {
            Methods = interfaceNode.Methods.Select(RewriteInterfaceMethod).ToList(),
            Attributes = RewriteAttributeApplications(interfaceNode.Attributes),
        };

    protected virtual RequirementNode RewriteRequirement(RequirementNode requirement) =>
        requirement with
        {
            GenericConstraints = RewriteGenericConstraints(requirement.GenericConstraints),
            Members = requirement.Members.Select(RewriteRequirementMember).ToList(),
        };

    protected virtual AttributeDeclarationNode RewriteAttributeDeclaration(AttributeDeclarationNode attribute) =>
        attribute;

    protected virtual TypeAliasNode RewriteTypeAlias(TypeAliasNode alias) =>
        alias with
        {
            Attributes = RewriteAttributeApplications(alias.Attributes),
            TargetTypeNode = RewriteType(alias.TargetTypeNode),
        };

    protected virtual EnumNode RewriteEnum(EnumNode enumNode) =>
        enumNode with
        {
            Members = enumNode.Members
                .Select(member => member with
                {
                    Attributes = RewriteAttributeApplications(member.Attributes),
                })
                .ToList(),
            Attributes = RewriteAttributeApplications(enumNode.Attributes),
        };

    protected virtual TestNode RewriteTest(TestNode test) =>
        test with
        {
            Body = RewriteStatements(test.Body),
            Attributes = RewriteAttributeApplications(test.Attributes),
        };

    protected virtual IReadOnlyList<StatementNode> RewriteStatements(IReadOnlyList<StatementNode> statements) =>
        statements.SelectMany(RewriteStatement).ToList();

    protected virtual IReadOnlyList<StatementNode> RewriteStatement(StatementNode statement) =>
        statement switch
        {
            MacroInvocationStatementNode invocation => [RewriteMacroInvocation(invocation)],
            CompileTimeLetStatementNode compileTimeLet => RewriteCompileTimeLetStatement(compileTimeLet),
            LetStatement let => RewriteLetStatement(let),
            UsingStatement usingStatement => RewriteUsingStatement(usingStatement),
            ReturnStatement ret => RewriteReturnStatement(ret),
            BreakStatement breakStatement => [breakStatement],
            ContinueStatement continueStatement => [continueStatement],
            CompileTimeIfStatementNode conditional => RewriteCompileTimeIfStatement(conditional),
            CompileTimeForeachStatementNode foreachNode => RewriteCompileTimeForeachStatement(foreachNode),
            IfStatement ifStatement => RewriteIfStatement(ifStatement),
            ElseBlockStatement elseBlock => RewriteElseBlockStatement(elseBlock),
            WhileStatement whileStatement => RewriteWhileStatement(whileStatement),
            ForStatement forStatement => RewriteForStatement(forStatement),
            ForeachStatement foreachStatement => RewriteForeachStatement(foreachStatement),
            SwitchStatement switchStatement => RewriteSwitchStatement(switchStatement),
            MatchStatement matchStatement => RewriteMatchStatement(matchStatement),
            CStatement c => RewriteCStatement(c),
            _ => [statement],
        };

    protected virtual MacroInvocationStatementNode RewriteMacroInvocation(MacroInvocationStatementNode invocation) =>
        invocation with { Arguments = invocation.Arguments.Select(RewriteMacroArgument).ToList() };

    protected virtual IReadOnlyList<StatementNode> RewriteCompileTimeLetStatement(
        CompileTimeLetStatementNode compileTimeLet) =>
        [compileTimeLet with { Initializer = RewriteRequiredExpression(compileTimeLet.Initializer) }];

    protected virtual IReadOnlyList<StatementNode> RewriteCompileTimeIfStatement(
        CompileTimeIfStatementNode conditional) =>
        [conditional with
        {
            Condition = RewriteRequiredExpression(conditional.Condition),
            ThenBody = RewriteStatements(conditional.ThenBody),
            ElseBody = RewriteStatements(conditional.ElseBody),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteCompileTimeForeachStatement(
        CompileTimeForeachStatementNode foreachNode) =>
        [foreachNode with
        {
            IterableExpression = RewriteRequiredExpression(foreachNode.IterableExpression),
            Body = RewriteStatements(foreachNode.Body),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteLetStatement(LetStatement let) =>
        [let with { Initializer = RewriteExpression(let.Initializer), TypeNode = RewriteType(let.TypeNode) }];

    protected virtual IReadOnlyList<StatementNode> RewriteUsingStatement(UsingStatement usingStatement) =>
        [usingStatement with
        {
            Initializer = RewriteRequiredExpression(usingStatement.Initializer),
            TypeNode = RewriteType(usingStatement.TypeNode),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteReturnStatement(ReturnStatement ret) =>
        [ret with { Expression = RewriteExpression(ret.Expression) }];

    protected virtual IReadOnlyList<StatementNode> RewriteIfStatement(IfStatement ifStatement) =>
        [ifStatement with
        {
            Condition = RewriteRequiredExpression(ifStatement.Condition),
            ThenBody = RewriteStatements(ifStatement.ThenBody),
            ElseBranch = RewriteSingleStatement(ifStatement.ElseBranch),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteElseBlockStatement(ElseBlockStatement elseBlock) =>
        [elseBlock with { Body = RewriteStatements(elseBlock.Body) }];

    protected virtual IReadOnlyList<StatementNode> RewriteWhileStatement(WhileStatement whileStatement) =>
        [whileStatement with
        {
            Condition = RewriteRequiredExpression(whileStatement.Condition),
            Body = RewriteStatements(whileStatement.Body),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteForStatement(ForStatement forStatement) =>
        [forStatement with
        {
            CachedRangeEndInitializer = RewriteForDeclarationInitializer(forStatement.CachedRangeEndInitializer),
            CounterInitializer = RewriteForDeclarationInitializer(forStatement.CounterInitializer),
            Initializer = RewriteForInitializer(forStatement.Initializer),
            Condition = RewriteRequiredExpression(forStatement.Condition),
            Increment = RewriteRequiredExpression(forStatement.Increment),
            CounterIncrement = RewriteExpression(forStatement.CounterIncrement),
            Body = RewriteStatements(forStatement.Body),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteForeachStatement(ForeachStatement foreachStatement) =>
        [foreachStatement with
        {
            IndexBinding = RewriteForeachBinding(foreachStatement.IndexBinding),
            KeyBinding = RewriteForeachBinding(foreachStatement.KeyBinding),
            ValueBinding = RewriteForeachBinding(foreachStatement.ValueBinding)!,
            IterableExpression = RewriteRequiredExpression(foreachStatement.IterableExpression),
            Body = RewriteStatements(foreachStatement.Body),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteSwitchStatement(SwitchStatement switchStatement) =>
        [switchStatement with
        {
            Expression = RewriteRequiredExpression(switchStatement.Expression),
            Cases = switchStatement.Cases.Select(RewriteSwitchCase).ToList(),
            DefaultBody = RewriteStatements(switchStatement.DefaultBody),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteMatchStatement(MatchStatement matchStatement) =>
        [matchStatement with
        {
            Expression = RewriteRequiredExpression(matchStatement.Expression),
            Arms = matchStatement.Arms.Select(RewriteMatchArm).ToList(),
        }];

    protected virtual IReadOnlyList<StatementNode> RewriteCStatement(CStatement c) =>
        [c with { Expression = RewriteRequiredExpression(c.Expression) }];

    protected virtual ExpressionNode? RewriteExpression(ExpressionNode? expression) =>
        expression switch
        {
            null => null,
            ErrorExpressionNode error => RewriteErrorExpression(error),
            PlaceholderExpressionNode placeholder => RewritePlaceholderExpression(placeholder),
            LiteralExpressionNode literal => RewriteLiteralExpression(literal),
            NameExpressionNode name => RewriteNameExpression(name),
            ParenthesizedExpressionNode parenthesized => RewriteParenthesizedExpression(parenthesized),
            CastExpressionNode cast => RewriteCastExpression(cast),
            UnaryExpressionNode unary => RewriteUnaryExpression(unary),
            PostfixExpressionNode postfix => RewritePostfixExpression(postfix),
            SizeOfExpressionNode sizeOf => RewriteSizeOfExpression(sizeOf),
            BinaryExpressionNode binary => RewriteBinaryExpression(binary),
            ConditionalExpressionNode conditional => RewriteConditionalExpression(conditional),
            TryExpressionNode attempt => RewriteTryExpression(attempt),
            ScalarRangeExpressionNode range => RewriteScalarRangeExpression(range),
            ListExpressionNode list => RewriteListExpression(list),
            TypeLiteralExpressionNode typeLiteral => RewriteTypeLiteralExpression(typeLiteral),
            InitializerExpressionNode initializer => RewriteInitializerExpression(initializer),
            FunctionExpressionNode function => RewriteFunctionExpression(function),
            AssignmentExpressionNode assignment => RewriteAssignmentExpression(assignment),
            CallExpressionNode call => RewriteCallExpression(call),
            GenericCallExpressionNode call => RewriteGenericCallExpression(call),
            MemberExpressionNode member => RewriteMemberExpression(member),
            ComputedMemberExpressionNode member => RewriteComputedMemberExpression(member),
            IndexExpressionNode index => RewriteIndexExpression(index),
            _ => expression,
        };

    protected virtual ExpressionNode RewriteErrorExpression(ErrorExpressionNode error) => error;

    protected virtual ExpressionNode RewritePlaceholderExpression(PlaceholderExpressionNode placeholder) =>
        placeholder with { Expression = RewriteRequiredExpression(placeholder.Expression) };

    protected virtual ExpressionNode RewriteLiteralExpression(LiteralExpressionNode literal) => literal;

    protected virtual ExpressionNode RewriteNameExpression(NameExpressionNode name) => name;

    protected virtual ExpressionNode RewriteParenthesizedExpression(ParenthesizedExpressionNode parenthesized) =>
        parenthesized with { Expression = RewriteRequiredExpression(parenthesized.Expression) };

    protected virtual ExpressionNode RewriteCastExpression(CastExpressionNode cast) =>
        cast with
        {
            Expression = RewriteRequiredExpression(cast.Expression),
            TargetTypeNode = RewriteType(cast.TargetTypeNode),
        };

    protected virtual ExpressionNode RewriteUnaryExpression(UnaryExpressionNode unary) =>
        unary with { Operand = RewriteRequiredExpression(unary.Operand) };

    protected virtual ExpressionNode RewritePostfixExpression(PostfixExpressionNode postfix) =>
        postfix with { Operand = RewriteRequiredExpression(postfix.Operand) };

    protected virtual ExpressionNode RewriteSizeOfExpression(SizeOfExpressionNode sizeOf) =>
        sizeOf with
        {
            Operand = RewriteSizeOfOperand(sizeOf.Operand),
        };

    protected virtual ExpressionNode RewriteBinaryExpression(BinaryExpressionNode binary) =>
        binary with
        {
            Left = RewriteRequiredExpression(binary.Left),
            Right = RewriteRequiredExpression(binary.Right),
        };

    protected virtual ExpressionNode RewriteConditionalExpression(ConditionalExpressionNode conditional) =>
        conditional with
        {
            Condition = RewriteRequiredExpression(conditional.Condition),
            WhenTrue = RewriteRequiredExpression(conditional.WhenTrue),
            WhenFalse = RewriteRequiredExpression(conditional.WhenFalse),
        };

    protected virtual ExpressionNode RewriteTryExpression(TryExpressionNode attempt) =>
        attempt with
        {
            Expression = RewriteRequiredExpression(attempt.Expression),
            Fallback = RewriteExpression(attempt.Fallback),
        };

    protected virtual ExpressionNode RewriteScalarRangeExpression(ScalarRangeExpressionNode range) =>
        range with
        {
            Start = RewriteRequiredExpression(range.Start),
            End = RewriteRequiredExpression(range.End),
        };

    protected virtual ExpressionNode RewriteListExpression(ListExpressionNode list) =>
        list with { Elements = list.Elements.Select(RewriteRequiredExpression).ToList() };

    protected virtual ExpressionNode RewriteTypeLiteralExpression(TypeLiteralExpressionNode typeLiteral) =>
        typeLiteral with { TypeNode = RewriteType(typeLiteral.TypeNode) ?? typeLiteral.TypeNode };

    protected virtual ExpressionNode RewriteInitializerExpression(InitializerExpressionNode initializer) =>
        initializer with
        {
            Fields = initializer.Fields.Select(field => field with { Value = RewriteRequiredExpression(field.Value) }).ToList(),
            Values = initializer.Values.Select(RewriteRequiredExpression).ToList(),
            TypeNameNode = RewriteType(initializer.TypeNameNode),
        };

    protected virtual ExpressionNode RewriteFunctionExpression(FunctionExpressionNode function) =>
        function with
        {
            Parameters = RewriteParameters(function.Parameters),
            ExpressionBody = RewriteExpression(function.ExpressionBody),
            BlockBody = function.BlockBody is null ? null : RewriteStatements(function.BlockBody),
            ReturnTypeNode = RewriteType(function.ReturnTypeNode),
        };

    protected virtual ExpressionNode RewriteAssignmentExpression(AssignmentExpressionNode assignment) =>
        assignment with
        {
            Target = RewriteRequiredExpression(assignment.Target),
            Value = RewriteRequiredExpression(assignment.Value),
        };

    protected virtual ExpressionNode RewriteCallExpression(CallExpressionNode call) =>
        call with
        {
            Callee = RewriteRequiredExpression(call.Callee),
            Arguments = call.Arguments.Select(RewriteRequiredExpression).ToList(),
        };

    protected virtual ExpressionNode RewriteGenericCallExpression(GenericCallExpressionNode call) =>
        call with
        {
            Callee = RewriteRequiredExpression(call.Callee),
            Arguments = call.Arguments.Select(RewriteRequiredExpression).ToList(),
            TypeArgumentNodes = RewriteTypes(call.TypeArgumentNodes),
        };

    protected virtual ExpressionNode RewriteMemberExpression(MemberExpressionNode member) =>
        member with { Target = RewriteRequiredExpression(member.Target) };

    protected virtual ExpressionNode RewriteComputedMemberExpression(ComputedMemberExpressionNode member) =>
        member with
        {
            Target = RewriteRequiredExpression(member.Target),
            MemberName = member.MemberName with
            {
                Expression = RewriteRequiredExpression(member.MemberName.Expression),
            },
        };

    protected virtual ExpressionNode RewriteIndexExpression(IndexExpressionNode index) =>
        index with
        {
            Target = RewriteRequiredExpression(index.Target),
            Index = RewriteRequiredExpression(index.Index),
        };

    protected virtual TypeNode? RewriteType(TypeNode? type) => type;

    protected virtual IReadOnlyList<TypeNode> RewriteTypes(IReadOnlyList<TypeNode> types) =>
        types.Select(type => RewriteType(type)!).ToList();

    protected virtual ParameterNode RewriteParameter(ParameterNode parameter) =>
        parameter with
        {
            Attributes = RewriteAttributeApplications(parameter.Attributes),
            TypeNode = RewriteType(parameter.TypeNode),
        };

    protected virtual IReadOnlyList<ParameterNode> RewriteParameters(IReadOnlyList<ParameterNode> parameters) =>
        parameters.Select(RewriteParameter).ToList();

    protected virtual ForInitializerNode RewriteForInitializer(ForInitializerNode initializer) =>
        initializer switch
        {
            ForDeclarationInitializerNode declaration => declaration with
            {
                Initializer = RewriteExpression(declaration.Initializer),
                TypeNode = RewriteType(declaration.TypeNode),
            },
            ForExpressionInitializerNode expression => expression with
            {
                Expression = RewriteRequiredExpression(expression.Expression),
            },
            _ => initializer,
        };

    protected virtual ForDeclarationInitializerNode? RewriteForDeclarationInitializer(ForDeclarationInitializerNode? initializer) =>
        initializer is null
            ? null
            : initializer with
            {
                Initializer = RewriteExpression(initializer.Initializer),
                TypeNode = RewriteType(initializer.TypeNode),
            };

    protected virtual ForeachBinding? RewriteForeachBinding(ForeachBinding? binding) =>
        binding is null ? null : binding with { TypeNode = RewriteType(binding.TypeNode) };

    protected virtual SwitchCaseNode RewriteSwitchCase(SwitchCaseNode switchCase) =>
        switchCase with
        {
            Pattern = RewriteRequiredExpression(switchCase.Pattern),
            Body = RewriteStatements(switchCase.Body),
        };

    protected virtual MatchArmNode RewriteMatchArm(MatchArmNode arm) =>
        arm with { Body = RewriteStatements(arm.Body) };

    protected virtual SizeOfOperandNode RewriteSizeOfOperand(SizeOfOperandNode operand) =>
        operand switch
        {
            SizeOfTypeOperandNode typeOperand => typeOperand with { TypeNode = RewriteType(typeOperand.TypeNode)! },
            SizeOfExpressionOperandNode expressionOperand => expressionOperand with
            {
                Expression = RewriteRequiredExpression(expressionOperand.Expression),
            },
            _ => operand,
        };

    protected virtual StructFieldNode RewriteStructField(StructFieldNode field) =>
        field with
        {
            Attributes = RewriteAttributeApplications(field.Attributes),
            TypeNode = RewriteType(field.TypeNode),
        };

    protected virtual StructRequirementNode RewriteStructRequirement(StructRequirementNode requirement) =>
        requirement with { TypeArgumentNodes = RewriteTypes(requirement.TypeArgumentNodes) };

    protected virtual GenericConstraintNode RewriteGenericConstraint(GenericConstraintNode constraint) =>
        constraint with { Requirements = constraint.Requirements.Select(RewriteStructRequirement).ToList() };

    protected virtual IReadOnlyList<GenericConstraintNode> RewriteGenericConstraints(IReadOnlyList<GenericConstraintNode> constraints) =>
        constraints.Select(RewriteGenericConstraint).ToList();

    protected virtual InterfaceMethodNode RewriteInterfaceMethod(InterfaceMethodNode method) =>
        method with
        {
            Parameters = RewriteParameters(method.Parameters),
            ReturnTypeNode = RewriteType(method.ReturnTypeNode),
        };

    protected virtual RequirementMemberNode RewriteRequirementMember(RequirementMemberNode member) =>
        member switch
        {
            RequirementFieldNode field => field with { TypeNode = RewriteType(field.TypeNode) },
            RequirementFunctionNode function => function with
            {
                Parameters = RewriteParameters(function.Parameters),
                ReturnTypeNode = RewriteType(function.ReturnTypeNode),
            },
            _ => member,
        };

    protected virtual TaggedUnionVariantNode RewriteTaggedUnionVariant(TaggedUnionVariantNode variant) =>
        variant with
        {
            Attributes = RewriteAttributeApplications(variant.Attributes),
            TypeNode = RewriteType(variant.TypeNode),
        };

    protected virtual IReadOnlyList<AttributeApplicationNode> RewriteAttributeApplications(
        IReadOnlyList<AttributeApplicationNode> attributes) =>
        attributes.Select(RewriteAttributeApplication).ToList();

    protected virtual AttributeApplicationNode RewriteAttributeApplication(AttributeApplicationNode attribute) =>
        attribute with
        {
            Arguments = attribute.Arguments
                .Select(argument => argument with
                {
                    Value = RewriteRequiredExpression(argument.Value),
                })
                .ToList(),
        };

    protected virtual ExposeMethodNode RewriteExposeMethod(ExposeMethodNode method) =>
        method with { ReturnTypeNode = RewriteType(method.ReturnTypeNode) };

    private StatementNode? RewriteSingleStatement(StatementNode? statement)
    {
        if (statement is null)
        {
            return null;
        }

        var rewritten = RewriteStatement(statement);
        return rewritten.Count switch
        {
            0 => null,
            1 => rewritten[0],
            _ => new ElseBlockStatement(statement.Location, rewritten),
        };
    }

    private ExpressionNode RewriteRequiredExpression(ExpressionNode expression) =>
        RewriteExpression(expression) ?? expression;
}
