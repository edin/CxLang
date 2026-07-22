using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic.Resolvers;

internal sealed class ExpressionTypeResolver(
    ProgramNode program,
    IReadOnlyList<string>? currentTypeParameters = null,
    IReadOnlyList<GenericConstraintNode>? currentGenericConstraints = null)
{
    private readonly IReadOnlyList<string> _currentTypeParameters = currentTypeParameters ?? [];
    private readonly IReadOnlyList<GenericConstraintNode> _currentGenericConstraints = currentGenericConstraints ?? [];
    private readonly TypeSyntaxTypeRefConverter _typeSyntaxConverter = new(program);
    private CallResolver? _callResolver;

    private CallResolver CallResolver => _callResolver ??= new CallResolver(
        program,
        ResolveTypeRef,
        _currentTypeParameters,
        _currentGenericConstraints);

    public TypeRef? ResolveTypeRef(ExpressionNode? expression, TypeEnvironment variables)
    {
        if (expression is null)
        {
            return null;
        }

        if (expression is SizeOfExpressionNode)
        {
            return ResolveKnownType(TypeRef.Usize);
        }

        if (expression is FunctionExpressionNode functionLiteral)
        {
            return ResolveFunctionExpressionTypeRef(functionLiteral);
        }

        if (expression.Semantic.Type is { } semanticType)
        {
            return semanticType;
        }

        return expression switch
        {
            LiteralExpressionNode literal => ResolveLiteralTypeRef(literal),
            NameExpressionNode name => ResolveNameTypeRef(name.Name, variables),
            ParenthesizedExpressionNode parenthesized => ResolveTypeRef(parenthesized.Expression, variables),
            CastExpressionNode cast => ResolveTypeNode(cast.TargetTypeNode),
            UnaryExpressionNode unary => ResolveUnaryTypeRef(unary, variables),
            PostfixExpressionNode postfix => ResolveTypeRef(postfix.Operand, variables),
            SizeOfExpressionNode => ResolveKnownType(TypeRef.Usize),
            BinaryExpressionNode binary => ResolveBinaryTypeRef(binary, variables),
            ScalarRangeExpressionNode range => ResolveRangeTypeRef(range, variables),
            ConditionalExpressionNode conditional => ResolveConditionalTypeRef(conditional, variables),
            TryExpressionNode attempt => ResolveTryTypeRef(attempt, variables),
            InitializerExpressionNode initializer => ResolveInitializerTypeRef(initializer, variables),
            FunctionExpressionNode functionExpression => ResolveFunctionExpressionTypeRef(functionExpression),
            AssignmentExpressionNode assignment => ResolveTypeRef(assignment.Target, variables),
            MemberExpressionNode member => ResolveMemberTypeRef(member, variables),
            CallExpressionNode call => ResolveCallTypeRef(call, variables),
            GenericCallExpressionNode call => ResolveGenericCallTypeRef(call, variables),
            IndexExpressionNode index => ResolveIndexTypeRef(index, variables),
            ErrorExpressionNode => null,
            _ => null,
        };
    }

    private TypeRef? ResolveLiteralTypeRef(LiteralExpressionNode literal) => literal.Kind switch
    {
        LiteralKind.Boolean => ResolveKnownType(TypeRef.Bool),
        LiteralKind.Null => new TypeRef.Null(),
        LiteralKind.String => new TypeRef.Pointer(ResolveKnownType(TypeRef.Char)),
        LiteralKind.Character => ResolveKnownType(TypeRef.Char),
        LiteralKind.Integer => ResolveKnownType(TypeRef.Int),
        LiteralKind.FloatingPoint => ResolveKnownType(TypeRef.Double),
        _ => null,
    };

    private TypeRef? ResolveTryTypeRef(
        TryExpressionNode attempt,
        TypeEnvironment variables)
    {
        var resultType = ResolveTypeRef(attempt.Expression, variables);
        resultType = resultType is null ? null : TypeRefFacts.UnwrapConst(TypeRefFacts.UnwrapAlias(resultType));
        if (resultType is TypeRef.Named
            {
                Name: "Result",
                Arguments: [var valueType, _],
            })
        {
            return valueType;
        }

        return attempt.Fallback is null
            ? null
            : ResolveTypeRef(attempt.Fallback, variables);
    }

    private TypeRef? ResolveNameTypeRef(string name, TypeEnvironment variables)
    {
        if (variables.TryGet(name, out var type))
        {
            return type;
        }

        var function = program.Functions.FirstOrDefault(function =>
            OwnerType(function) is null
            && function.TypeParameters.Count == 0
            && function.Name == name);
        if (function is not null)
        {
            return GetFunctionTypeRef(function.Parameters, function.ReturnTypeNode);
        }

        var externFunction = program.ExternFunctions.FirstOrDefault(function =>
            function.Name == name
            && function.TypeParameters.Count == 0);
        return externFunction is null
            ? null
            : GetFunctionTypeRef(externFunction.Parameters, externFunction.ReturnTypeNode);
    }

    private TypeRef? ResolveTypeNode(TypeNode? typeNode) =>
        typeNode?.Semantic.Type
        ?? (typeNode is null ? null : _typeSyntaxConverter.Convert(typeNode))
        ?? new TypeRef.Unknown();

    private TypeRef? ResolveUnaryTypeRef(UnaryExpressionNode unary, TypeEnvironment variables)
    {
        var operandType = ResolveTypeRef(unary.Operand, variables);
        if (operandType is null)
        {
            return null;
        }

        return unary.Operator switch
        {
            UnaryOperator.AddressOf => new TypeRef.Pointer(operandType),
            UnaryOperator.Dereference => UnwrapPointer(operandType),
            UnaryOperator.LogicalNot => ResolveKnownType(TypeRef.Bool),
            UnaryOperator.Plus or UnaryOperator.Negate => operandType,
            _ => null,
        };
    }

    private TypeRef? ResolveBinaryTypeRef(BinaryExpressionNode binary, TypeEnvironment variables)
    {
        if (binary.Operator is BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.LessThanOrEqual
            or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterThanOrEqual
            or BinaryOperator.LogicalAnd
            or BinaryOperator.LogicalOr)
        {
            return ResolveKnownType(TypeRef.Bool);
        }

        if (binary.Operator == BinaryOperator.Compare)
        {
            return ResolveKnownType(TypeRef.Int);
        }

        var left = ResolveTypeRef(binary.Left, variables);
        var right = ResolveTypeRef(binary.Right, variables);
        return SameType(left, right) ? left : left ?? right;
    }

    private TypeRef? ResolveConditionalTypeRef(ConditionalExpressionNode conditional, TypeEnvironment variables)
    {
        var whenTrue = ResolveTypeRef(conditional.WhenTrue, variables);
        var whenFalse = ResolveTypeRef(conditional.WhenFalse, variables);
        return SameType(whenTrue, whenFalse) ? whenTrue : whenTrue ?? whenFalse;
    }

    private TypeRef? ResolveRangeTypeRef(ScalarRangeExpressionNode range, TypeEnvironment variables)
    {
        var start = ResolveTypeRef(range.Start, variables);
        var end = ResolveTypeRef(range.End, variables);
        if (SameType(start, end))
        {
            return start;
        }

        if (TypeRefFacts.IsNamed(start, "int") && IsIntegerLiteral(range.Start) && end is not null)
        {
            return end;
        }

        if (TypeRefFacts.IsNamed(end, "int") && IsIntegerLiteral(range.End) && start is not null)
        {
            return start;
        }

        return start ?? end;
    }

    private static bool IsIntegerLiteral(ExpressionNode expression) =>
        expression is LiteralExpressionNode { Kind: LiteralKind.Integer };

    private TypeRef? ResolveInitializerTypeRef(InitializerExpressionNode initializer, TypeEnvironment variables)
    {
        if (initializer.TypeNameNode is not null)
        {
            return ResolveTypeNode(initializer.TypeNameNode);
        }

        if (initializer.Values.Count == 0)
        {
            return null;
        }

        var firstType = ResolveTypeRef(initializer.Values[0], variables);
        return initializer.Values
            .Skip(1)
            .Select(value => ResolveTypeRef(value, variables))
            .All(type => SameType(firstType, type))
            ? firstType
            : null;
    }

    private TypeRef ResolveFunctionExpressionTypeRef(FunctionExpressionNode functionExpression)
    {
        var parameters = functionExpression.Parameters
            .Select(parameter => ResolveTypeNode(parameter.TypeNode) ?? new TypeRef.Unknown())
            .ToList();
        TypeRef? defaultReturnType = functionExpression.ReturnTypeNode is null
            ? ResolveKnownType(TypeRef.Int)
            : null;
        var returnType = ResolveTypeNode(functionExpression.ReturnTypeNode)
            ?? defaultReturnType
            ?? new TypeRef.Unknown();
        return new TypeRef.Function(parameters, returnType);
    }

    private TypeRef GetFunctionTypeRef(IReadOnlyList<ParameterNode> parameters, TypeNode? returnTypeNode) =>
        new TypeRef.Function(
            parameters
                .Where(parameter => !parameter.IsVariadic)
                .Select(parameter => ResolveTypeNode(parameter.TypeNode) ?? new TypeRef.Unknown())
                .ToList(),
            ResolveTypeNode(returnTypeNode) ?? new TypeRef.Unknown(),
            parameters.Any(parameter => parameter.IsVariadic));

    private TypeRef? ResolveMemberTypeRef(MemberExpressionNode member, TypeEnvironment variables)
    {
        var targetType = ResolveTypeRef(member.Target, variables);
        if (targetType is null)
        {
            var staticFunctionType = ResolveStaticFunctionReference(member);
            if (staticFunctionType is not null)
            {
                return staticFunctionType;
            }

            var qualifiedName = ExpressionNameFacts.GetQualifiedName(member);
            var global = program.GlobalVariables.FirstOrDefault(global =>
                string.Equals(global.Name, qualifiedName, StringComparison.Ordinal));
            return ResolveTypeNode(global?.TypeNode);
        }

        var normalizedType = TypeRefFacts.StripPointersAndAliases(targetType);
        var normalizedTypeText = TypeRefFormatter.ToCxString(normalizedType);
        var normalizedTypeName = TypeRefFacts.GetBaseName(normalizedType);

        var structNode = ResolveStruct(normalizedType);
        var field = structNode?.Fields.FirstOrDefault(field => field.Name == member.MemberName);
        if (field is not null)
        {
            return ResolveTypeNode(field.TypeNode);
        }

        var union = program.TaggedUnions.FirstOrDefault(union => union.Name == normalizedTypeName);
        var variant = union?.Variants.FirstOrDefault(variant => variant.Name == member.MemberName);
        if (variant is not null)
        {
            return ResolveTypeNode(variant.TypeNode);
        }

        var interfaceNode = program.Interfaces.FirstOrDefault(interfaceNode => interfaceNode.Name == normalizedTypeName);
        if (interfaceNode is not null)
        {
            if (member.MemberName == "state")
            {
                return new TypeRef.Pointer(TypeRef.Void);
            }

            var method = interfaceNode.Methods.FirstOrDefault(method => "v_" + method.Name == member.MemberName);
            if (method is not null)
            {
                var parameterTypes = new[] { new TypeRef.Pointer(TypeRef.Void) }
                    .Concat(method.Parameters.Select(parameter => ResolveTypeNode(parameter.TypeNode) ?? new TypeRef.Unknown()))
                    .ToList();
                return new TypeRef.Function(
                    parameterTypes,
                    ResolveTypeNode(method.ReturnTypeNode) ?? new TypeRef.Unknown());
            }
        }

        return null;
    }

    private TypeRef? ResolveStaticFunctionReference(MemberExpressionNode member)
    {
        var targetName = ExpressionNameFacts.GetQualifiedName(member.Target);
        if (targetName is null)
        {
            return null;
        }

        var function = program.Functions.FirstOrDefault(function =>
            function.IsStatic
            && OwnerType(function) is not null
            && function.TypeParameters.Count == 0
            && targetName == OwnerType(function)
            && function.Name == member.MemberName);
        return function is null
            ? null
            : GetFunctionTypeRef(function.Parameters, function.ReturnTypeNode);
    }

    private TypeRef? ResolveIndexTypeRef(IndexExpressionNode index, TypeEnvironment variables)
    {
        var targetType = ResolveTypeRef(index.Target, variables);
        return targetType switch
        {
            TypeRef.FixedArray fixedArray => fixedArray.Element,
            TypeRef.Pointer pointer => pointer.Element,
            TypeRef.Alias alias => ResolveIndexTypeRef(alias.Target),
            _ => null,
        };
    }

    private static TypeRef? ResolveIndexTypeRef(TypeRef targetType) => targetType switch
    {
        TypeRef.FixedArray fixedArray => fixedArray.Element,
        TypeRef.Pointer pointer => pointer.Element,
        TypeRef.Alias alias => ResolveIndexTypeRef(alias.Target),
        _ => null,
    };

    private TypeRef? ResolveCallTypeRef(CallExpressionNode call, TypeEnvironment variables) =>
        CallResolver.ResolveTypeRefs(call.Callee, [], call.Arguments, variables)?.ReturnType;

    private TypeRef? ResolveGenericCallTypeRef(GenericCallExpressionNode call, TypeEnvironment variables) =>
        CallResolver.ResolveTypeRefs(call.Callee, TypeArgumentRefs(call.TypeArgumentNodes), call.Arguments, variables) is { } resolvedCall
            ? resolvedCall.ReturnType
            : null;

    internal IReadOnlyList<TypeRef>? InferFunctionTypeArgumentRefs(
        IReadOnlyList<string> typeParameters,
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<ExpressionNode> arguments,
        TypeEnvironment variables,
        bool skipSelf,
        IReadOnlyList<TypeRef>? seedArguments = null)
    {
        if (typeParameters.Count == 0)
        {
            return [];
        }

        var fixedParameters = parameters
            .Skip(skipSelf ? 1 : 0)
            .Where(parameter => !parameter.IsVariadic)
            .ToList();
        if (arguments.Count < fixedParameters.Count)
        {
            return null;
        }

        var bindings = new TypeBindings();
        if (seedArguments is not null && seedArguments.Count == typeParameters.Count)
        {
            foreach (var (parameter, argument) in typeParameters.Zip(seedArguments))
            {
                bindings.Set(parameter, argument);
            }
        }

        for (var i = 0; i < fixedParameters.Count; i++)
        {
            var argumentType = ResolveTypeRef(arguments[i], variables);
            if (argumentType is null)
            {
                return null;
            }

            if (!TryBindType(ResolveTypeNode(fixedParameters[i].TypeNode), argumentType, typeParameters, bindings))
            {
                return null;
            }
        }

        return typeParameters.All(parameter => bindings.Bindings.ContainsKey(parameter))
            ? typeParameters.Select(parameter => bindings.Bindings[parameter]).ToList()
            : null;
    }

    private static bool TryBindType(
        TypeRef? parameterType,
        TypeRef? argumentType,
        IReadOnlyList<string> typeParameters,
        TypeBindings bindings)
    {
        if (parameterType is null || argumentType is null)
        {
            return true;
        }

        parameterType = TypeRefFacts.UnwrapAlias(parameterType);
        argumentType = TypeRefFacts.UnwrapAlias(argumentType);

        if (parameterType is TypeRef.Const parameterConst)
        {
            return TryBindType(
                parameterConst.Element,
                argumentType is TypeRef.Const argumentConst ? argumentConst.Element : argumentType,
                typeParameters,
                bindings);
        }

        if (parameterType is TypeRef.Named { Arguments.Count: 0 } parameterNamed
            && typeParameters.Contains(parameterNamed.Name, StringComparer.Ordinal))
        {
            return Bind(parameterNamed.Name, argumentType, bindings);
        }

        if (TypeRefFacts.TryGetPointerElement(parameterType, out var parameterElement)
            && TypeRefFacts.TryGetPointerElement(argumentType, out var argumentElement))
        {
            return TryBindType(parameterElement, argumentElement, typeParameters, bindings);
        }

        if (parameterType is TypeRef.Named parameterGeneric
            && argumentType is TypeRef.Named argumentGeneric
            && string.Equals(parameterGeneric.Name, argumentGeneric.Name, StringComparison.Ordinal)
            && parameterGeneric.Arguments.Count > 0
            && parameterGeneric.Arguments.Count == argumentGeneric.Arguments.Count)
        {
            var parameterArguments = parameterGeneric.Arguments;
            var argumentArguments = argumentGeneric.Arguments;
            for (var i = 0; i < parameterArguments.Count; i++)
            {
                if (!TryBindType(parameterArguments[i], argumentArguments[i], typeParameters, bindings))
                {
                    return false;
                }
            }

            return true;
        }

        if (parameterType is TypeRef.FixedArray parameterArray
            && argumentType is TypeRef.FixedArray argumentArray
            && parameterArray.Length == argumentArray.Length)
        {
            return TryBindType(parameterArray.Element, argumentArray.Element, typeParameters, bindings);
        }

        if (parameterType is TypeRef.Function parameterFunction
            && argumentType is TypeRef.Function argumentFunction
            && parameterFunction.Parameters.Count == argumentFunction.Parameters.Count
            && parameterFunction.IsVariadic == argumentFunction.IsVariadic)
        {
            for (var i = 0; i < parameterFunction.Parameters.Count; i++)
            {
                if (!TryBindType(parameterFunction.Parameters[i], argumentFunction.Parameters[i], typeParameters, bindings))
                {
                    return false;
                }
            }

            return TryBindType(parameterFunction.ReturnType, argumentFunction.ReturnType, typeParameters, bindings);
        }

        return true;
    }

    private static bool Bind(string typeParameter, TypeRef typeArgument, TypeBindings bindings)
    {
        if (!bindings.TryGet(typeParameter, out var existing))
        {
            bindings.Set(typeParameter, typeArgument);
            return true;
        }

        return TypeIdentity.ResolvedEquals(existing, typeArgument);
    }

    private StructNode? ResolveStruct(TypeRef type)
    {
        if (TypeRefFacts.TryGetNamed(type, out var namedType)
            && namedType.Arguments.Count > 0)
        {
            var definition = program.Structs.FirstOrDefault(structNode =>
                structNode.Name == namedType.Name
                && structNode.TypeParameters.Count == namedType.Arguments.Count);
            if (definition is null)
            {
                return null;
            }

            var substitutions = definition.TypeParameters
                .Zip(namedType.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            return definition with
            {
                Fields = definition.Fields
                    .Select(field =>
                    {
                        var substitutedType = TypeRefRewriter.Substitute(
                            ResolveTypeNode(field.TypeNode) ?? new TypeRef.Unknown(),
                            substitutions);
                        return field with
                        {
                            TypeNode = substitutedType.ToTypeNode(field.Location),
                        };
                    })
                    .ToList(),
            };
        }

        var typeName = TypeRefFacts.GetBaseName(type);
        var structNode = program.Structs.FirstOrDefault(structNode =>
            structNode.Name == typeName
            && structNode.TypeParameters.Count == 0);
        if (structNode is not null)
        {
            return structNode;
        }

        if (TypeRefFacts.TryGetNamed(type, out var adapterType)
            && program.TypeAdapters.FirstOrDefault(adapter =>
                adapter.Name == adapterType.Name
                && adapter.TypeParameters.Count == adapterType.Arguments.Count) is { } adapter
            && ResolveTypeNode(adapter.BaseTypeNode) is { } baseType)
        {
            var substitutions = adapter.TypeParameters
                .Zip(adapterType.Arguments)
                .ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
            return ResolveStruct(TypeRefRewriter.Substitute(baseType, substitutions));
        }

        return null;
    }

    private static TypeRef? UnwrapPointer(TypeRef type) =>
        TypeRefFacts.TryGetPointerElement(type, out var element) ? element : null;

    private static bool SameType(TypeRef? left, TypeRef? right) =>
        TypeIdentity.ResolvedEquals(left, right);

    private TypeRef ResolveKnownType(TypeRef.Named type) =>
        _typeSyntaxConverter.Convert(new NamedTypeSyntaxNode(type.Name));

    private string? OwnerType(FunctionNode function) =>
        TypeRefFacts.GetBaseName(ResolveTypeNode(function.OwnerTypeNode));

    private IReadOnlyList<TypeRef> TypeArgumentRefs(IReadOnlyList<TypeNode> typeArgumentNodes) =>
        typeArgumentNodes.Select(typeNode => ResolveTypeNode(typeNode) ?? new TypeRef.Unknown()).ToList();

}
