using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeRefModuleResolver
{
    private static readonly IReadOnlySet<string> EmptyTypeParameters = new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _modulesByTypeName = new(StringComparer.Ordinal);
    private string? _programModuleName;

    public void Resolve(ProgramNode program)
    {
        _modulesByTypeName.Clear();
        _programModuleName = program.Module?.Name;
        IndexTypeDeclarations(program);

        foreach (var typeAlias in program.TypeAliases)
        {
            ResolveType(typeAlias, typeAlias.TargetTypeNode, EmptyTypeParameters);
        }

        foreach (var declaration in program.CDeclarations)
        {
            foreach (var typeAlias in declaration.TypeAliases)
            {
                ResolveType(typeAlias, typeAlias.TargetTypeNode, EmptyTypeParameters);
            }

            foreach (var externFunction in declaration.Functions)
            {
                ResolveFunctionSignature(
                    externFunction,
                    externFunction.ReturnTypeNode,
                    externFunction.Parameters,
                    externFunction.TypeParameters.ToHashSet(StringComparer.Ordinal));
            }

            foreach (var constant in declaration.Constants)
            {
                ResolveType(constant, constant.TypeNode, EmptyTypeParameters);
            }

            foreach (var structNode in declaration.Structs)
            {
                ResolveStruct(structNode);
            }

            foreach (var union in declaration.Unions)
            {
                ResolveUnion(union);
            }
        }

        foreach (var externFunction in program.ExternFunctions)
        {
            ResolveFunctionSignature(
                externFunction,
                externFunction.ReturnTypeNode,
                externFunction.Parameters,
                externFunction.TypeParameters.ToHashSet(StringComparer.Ordinal));
        }

        foreach (var global in program.GlobalVariables)
        {
            ResolveType(global, global.TypeNode, EmptyTypeParameters);
        }

        foreach (var requirement in program.Requirements)
        {
            var typeParameters = requirement.TypeParameters.ToHashSet(StringComparer.Ordinal);
            ResolveGenericConstraints(requirement.GenericConstraints, typeParameters);
            foreach (var member in requirement.Members)
            {
                if (member is RequirementFunctionNode function)
                {
                    ResolveFunctionSignature(function, function.ReturnTypeNode, function.Parameters, typeParameters);
                }
                else if (member is RequirementFieldNode field)
                {
                    ResolveType(field, field.TypeNode, typeParameters);
                }
            }
        }

        foreach (var interfaceNode in program.Interfaces)
        {
            foreach (var method in interfaceNode.Methods)
            {
                ResolveFunctionSignature(method, method.ReturnTypeNode, method.Parameters, EmptyTypeParameters);
            }
        }

        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            foreach (var field in enumNode.DataFields ?? [])
            {
                ResolveType(field, field.TypeNode, EmptyTypeParameters);
            }
        }

        foreach (var structNode in program.Structs)
        {
            ResolveStruct(structNode);
        }

        foreach (var adapter in program.TypeAdapters)
        {
            var typeParameters = adapter.TypeParameters.ToHashSet(StringComparer.Ordinal);
            ResolveType(adapter, adapter.BaseTypeNode, typeParameters);
            foreach (var expose in adapter.ExposedMethods)
            {
                ResolveType(expose, expose.ReturnTypeNode, typeParameters);
            }

            foreach (var method in adapter.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var extension in program.Extensions)
        {
            var typeParameters = extension.TypeParameters.ToHashSet(StringComparer.Ordinal);
            ResolveType(extension, extension.TargetTypeNode, typeParameters);
            ResolveGenericConstraints(extension.GenericConstraints, typeParameters);
            foreach (var method in extension.Methods)
            {
                ResolveFunction(method);
            }
        }

        foreach (var union in program.TaggedUnions)
        {
            ResolveUnion(union);
        }

        foreach (var function in program.Functions)
        {
            ResolveFunction(function);
        }
    }

    private void IndexTypeDeclarations(ProgramNode program)
    {
        foreach (var typeAlias in program.TypeAliases.Concat(program.CDeclarations.SelectMany(declaration => declaration.TypeAliases)))
        {
            AddType(typeAlias.Name, ModuleName(typeAlias));
        }

        foreach (var structNode in program.Structs.Concat(program.CDeclarations.SelectMany(declaration => declaration.Structs)))
        {
            AddType(structNode.Name, ModuleName(structNode));
        }

        foreach (var enumNode in program.Enums.Concat(program.CDeclarations.SelectMany(declaration => declaration.Enums)))
        {
            AddType(enumNode.Name, ModuleName(enumNode));
        }

        foreach (var union in program.TaggedUnions.Concat(program.CDeclarations.SelectMany(declaration => declaration.Unions)))
        {
            AddType(union.Name, ModuleName(union));
        }

        foreach (var adapter in program.TypeAdapters)
        {
            AddType(adapter.Name, ModuleName(adapter));
        }
    }

    private void AddType(string name, string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(moduleName))
        {
            return;
        }

        if (!_modulesByTypeName.TryGetValue(name, out var modules))
        {
            modules = new HashSet<string>(StringComparer.Ordinal);
            _modulesByTypeName[name] = modules;
        }

        modules.Add(moduleName);
    }

    private void ResolveStruct(StructNode structNode)
    {
        var typeParameters = structNode.TypeParameters.ToHashSet(StringComparer.Ordinal);
        ResolveGenericConstraints(structNode.GenericConstraints, typeParameters);
        ResolveStructRequirements(structNode.Requirements, typeParameters);
        foreach (var field in structNode.Fields)
        {
            ResolveType(field, field.TypeNode, typeParameters);
        }

        foreach (var method in structNode.Methods)
        {
            ResolveFunction(method);
        }
    }

    private void ResolveUnion(TaggedUnionNode union)
    {
        foreach (var variant in union.Variants)
        {
            ResolveType(variant, variant.TypeNode, EmptyTypeParameters);
        }

        foreach (var method in union.Methods)
        {
            ResolveFunction(method);
        }
    }

    private void ResolveFunction(FunctionNode function)
    {
        var typeParameters = function.TypeParameters.ToHashSet(StringComparer.Ordinal);
        ResolveType(function, function.OwnerTypeNode, typeParameters);
        ResolveGenericConstraints(function.GenericConstraints, typeParameters);
        ResolveFunctionSignature(function, function.ReturnTypeNode, function.Parameters, typeParameters);
        ResolveStatements(function.Body, typeParameters);
    }

    private void ResolveFunctionSignature(
        Syntax.SyntaxNode node,
        TypeNode? returnTypeNode,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlySet<string> typeParameters)
    {
        ResolveType(node, returnTypeNode, typeParameters);
        foreach (var parameter in parameters.Where(parameter => !parameter.IsVariadic))
        {
            ResolveType(parameter, parameter.TypeNode, typeParameters);
        }
    }

    private void ResolveGenericConstraints(
        IReadOnlyList<GenericConstraintNode> constraints,
        IReadOnlySet<string> typeParameters)
    {
        foreach (var constraint in constraints)
        {
            ResolveStructRequirements(constraint.Requirements, typeParameters);
        }
    }

    private void ResolveStructRequirements(
        IReadOnlyList<StructRequirementNode> requirements,
        IReadOnlySet<string> typeParameters)
    {
        foreach (var requirement in requirements)
        {
            foreach (var typeArgument in requirement.TypeArgumentNodes)
            {
                ResolveType(typeArgument, typeArgument, typeParameters);
            }
        }
    }

    private void ResolveStatements(
        IReadOnlyList<StatementNode> statements,
        IReadOnlySet<string> typeParameters)
    {
        foreach (var statement in statements)
        {
            ResolveStatement(statement, typeParameters);
        }
    }

    private void ResolveStatement(
        StatementNode statement,
        IReadOnlySet<string> typeParameters)
    {
        switch (statement)
        {
            case LetStatement let:
                ResolveType(let, let.TypeNode, typeParameters);
                ResolveExpression(let.Initializer, typeParameters);
                break;
            case ReturnStatement { Expression: not null } ret:
                ResolveExpression(ret.Expression, typeParameters);
                break;
            case CStatement c:
                ResolveExpression(c.Expression, typeParameters);
                break;
            case IfStatement ifStatement:
                ResolveExpression(ifStatement.Condition, typeParameters);
                ResolveStatements(ifStatement.ThenBody, typeParameters);
                if (ifStatement.ElseBranch is not null)
                {
                    ResolveStatement(ifStatement.ElseBranch, typeParameters);
                }

                break;
            case ElseBlockStatement elseBlock:
                ResolveStatements(elseBlock.Body, typeParameters);
                break;
            case WhileStatement whileStatement:
                ResolveExpression(whileStatement.Condition, typeParameters);
                ResolveStatements(whileStatement.Body, typeParameters);
                break;
            case ForStatement forStatement:
                ResolveOptionalForInitializer(forStatement.CachedRangeEndInitializer, typeParameters);
                ResolveOptionalForInitializer(forStatement.CounterInitializer, typeParameters);
                ResolveForInitializer(forStatement.Initializer, typeParameters);
                ResolveExpression(forStatement.Condition, typeParameters);
                ResolveExpression(forStatement.Increment, typeParameters);
                ResolveExpression(forStatement.CounterIncrement, typeParameters);
                ResolveStatements(forStatement.Body, typeParameters);
                break;
            case ForeachStatement foreachStatement:
                ResolveForeachBinding(foreachStatement.IndexBinding, typeParameters);
                ResolveForeachBinding(foreachStatement.KeyBinding, typeParameters);
                ResolveForeachBinding(foreachStatement.ValueBinding, typeParameters);
                ResolveExpression(foreachStatement.IterableExpression, typeParameters);
                ResolveStatements(foreachStatement.Body, typeParameters);
                break;
            case SwitchStatement switchStatement:
                ResolveExpression(switchStatement.Expression, typeParameters);
                foreach (var switchCase in switchStatement.Cases)
                {
                    ResolveExpression(switchCase.Pattern, typeParameters);
                    ResolveStatements(switchCase.Body, typeParameters);
                }

                ResolveStatements(switchStatement.DefaultBody, typeParameters);
                break;
            case MatchStatement matchStatement:
                ResolveExpression(matchStatement.Expression, typeParameters);
                foreach (var arm in matchStatement.Arms)
                {
                    ResolveStatements(arm.Body, typeParameters);
                }

                break;
        }
    }

    private void ResolveForInitializer(
        ForInitializerNode initializer,
        IReadOnlySet<string> typeParameters)
    {
        switch (initializer)
        {
            case ForDeclarationInitializerNode declaration:
                ResolveType(declaration, declaration.TypeNode, typeParameters);
                ResolveExpression(declaration.Initializer, typeParameters);
                break;
            case ForExpressionInitializerNode expression:
                ResolveExpression(expression.Expression, typeParameters);
                break;
        }
    }

    private void ResolveOptionalForInitializer(
        ForInitializerNode? initializer,
        IReadOnlySet<string> typeParameters)
    {
        if (initializer is not null)
        {
            ResolveForInitializer(initializer, typeParameters);
        }
    }

    private void ResolveForeachBinding(
        ForeachBinding? binding,
        IReadOnlySet<string> typeParameters)
    {
        if (binding is not null)
        {
            ResolveType(binding, binding.TypeNode, typeParameters);
        }
    }

    private void ResolveExpression(
        ExpressionNode? expression,
        IReadOnlySet<string> typeParameters)
    {
        switch (expression)
        {
            case null:
                return;
            case ParenthesizedExpressionNode parenthesized:
                ResolveExpression(parenthesized.Expression, typeParameters);
                break;
            case CastExpressionNode cast:
                ResolveType(cast, cast.TargetTypeNode, typeParameters);
                ResolveExpression(cast.Expression, typeParameters);
                break;
            case UnaryExpressionNode unary:
                ResolveExpression(unary.Operand, typeParameters);
                break;
            case PostfixExpressionNode postfix:
                ResolveExpression(postfix.Operand, typeParameters);
                break;
            case SizeOfExpressionNode { Operand: SizeOfTypeOperandNode operand } sizeOf:
                ResolveType(sizeOf, operand.TypeNode, typeParameters);
                break;
            case SizeOfExpressionNode { Operand: SizeOfExpressionOperandNode operand }:
                ResolveExpression(operand.Expression, typeParameters);
                break;
            case BinaryExpressionNode binary:
                ResolveExpression(binary.Left, typeParameters);
                ResolveExpression(binary.Right, typeParameters);
                break;
            case ConditionalExpressionNode conditional:
                ResolveExpression(conditional.Condition, typeParameters);
                ResolveExpression(conditional.WhenTrue, typeParameters);
                ResolveExpression(conditional.WhenFalse, typeParameters);
                break;
            case TryExpressionNode attempt:
                ResolveExpression(attempt.Expression, typeParameters);
                ResolveExpression(attempt.Fallback, typeParameters);
                break;
            case ScalarRangeExpressionNode range:
                ResolveExpression(range.Start, typeParameters);
                ResolveExpression(range.End, typeParameters);
                break;
            case InitializerExpressionNode initializer:
                ResolveType(initializer, initializer.TypeNameNode, typeParameters);
                foreach (var field in initializer.Fields)
                {
                    ResolveExpression(field.Value, typeParameters);
                }

                foreach (var value in initializer.Values)
                {
                    ResolveExpression(value, typeParameters);
                }

                break;
            case FunctionExpressionNode function:
                ResolveFunctionSignature(function, function.ReturnTypeNode, function.Parameters, typeParameters);
                ResolveExpression(function.ExpressionBody, typeParameters);
                if (function.BlockBody is not null)
                {
                    ResolveStatements(function.BlockBody, typeParameters);
                }

                break;
            case AssignmentExpressionNode assignment:
                ResolveExpression(assignment.Target, typeParameters);
                ResolveExpression(assignment.Value, typeParameters);
                break;
            case CallExpressionNode call:
                ResolveExpression(call.Callee, typeParameters);
                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, typeParameters);
                }

                break;
            case GenericCallExpressionNode call:
                ResolveExpression(call.Callee, typeParameters);
                foreach (var typeArgument in call.TypeArgumentNodes)
                {
                    ResolveType(typeArgument, typeArgument, typeParameters);
                }

                foreach (var argument in call.Arguments)
                {
                    ResolveExpression(argument, typeParameters);
                }

                break;
            case MemberExpressionNode member:
                ResolveExpression(member.Target, typeParameters);
                break;
            case IncompleteMemberExpressionNode member:
                ResolveExpression(member.Target, typeParameters);
                break;
            case IndexExpressionNode index:
                ResolveExpression(index.Target, typeParameters);
                ResolveExpression(index.Index, typeParameters);
                break;
        }
    }

    private void ResolveType(
        Syntax.SyntaxNode node,
        TypeNode? typeNode,
        IReadOnlySet<string> typeParameters)
    {
        if (node.Semantic.Type is not null)
        {
            node.Semantic.Type = Qualify(node.Semantic.Type, CurrentModule(node) ?? _programModuleName, typeParameters);
        }

        if (typeNode?.Semantic.Type is not null)
        {
            typeNode.Semantic.Type = Qualify(typeNode.Semantic.Type, CurrentModule(node) ?? _programModuleName, typeParameters);
            node.Semantic.Type = typeNode.Semantic.Type;
        }
    }

    private TypeRef Qualify(
        TypeRef type,
        string? currentModule,
        IReadOnlySet<string> typeParameters) =>
        type switch
        {
            TypeRef.Named named => QualifyNamed(named, currentModule, typeParameters),
            TypeRef.Alias alias => new TypeRef.Alias(alias.Name, Qualify(alias.Target, currentModule, typeParameters)),
            TypeRef.Pointer pointer => new TypeRef.Pointer(Qualify(pointer.Element, currentModule, typeParameters)),
            TypeRef.Const constType => new TypeRef.Const(Qualify(constType.Element, currentModule, typeParameters)),
            TypeRef.FixedArray array => new TypeRef.FixedArray(Qualify(array.Element, currentModule, typeParameters), array.Length),
            TypeRef.Function function => new TypeRef.Function(
                function.Parameters.Select(parameter => Qualify(parameter, currentModule, typeParameters)).ToList(),
                Qualify(function.ReturnType, currentModule, typeParameters),
                function.IsVariadic),
            _ => type,
        };

    private TypeRef.Named QualifyNamed(
        TypeRef.Named named,
        string? currentModule,
        IReadOnlySet<string> typeParameters)
    {
        var arguments = named.Arguments
            .Select(argument => Qualify(argument, currentModule, typeParameters))
            .ToList();
        var moduleName = named.ModuleName ?? ResolveModuleName(named.Name, currentModule, typeParameters);
        return named with
        {
            Arguments = arguments,
            ModuleName = moduleName,
        };
    }

    private string? ResolveModuleName(
        string name,
        string? currentModule,
        IReadOnlySet<string> typeParameters)
    {
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name, "Self", StringComparison.Ordinal)
            || BuiltinTypes.IsBuiltin(name)
            || typeParameters.Contains(name))
        {
            return null;
        }

        if (!_modulesByTypeName.TryGetValue(name, out var modules))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(currentModule) && modules.Contains(currentModule))
        {
            return currentModule;
        }

        return modules.Count == 1 ? modules.Single() : null;
    }

    private static string? CurrentModule(Syntax.SyntaxNode node) =>
        string.IsNullOrWhiteSpace(node.Semantic.ModuleName) ? null : node.Semantic.ModuleName;

    private string? ModuleName(Syntax.SyntaxNode node) => CurrentModule(node) ?? _programModuleName;
}
