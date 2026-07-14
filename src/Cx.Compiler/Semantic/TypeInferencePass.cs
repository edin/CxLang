using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic.Resolvers;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Semantic;

internal sealed class TypeInferencePass(DiagnosticBag diagnostics)
{
    private ExpressionTypeResolver? _resolver;
    private TypeSystem? _typeSystem;
    private TypeRefParser? _typeRefParser;
    private ProgramNode? _program;
    private TypeEnvironment _globalTypeEnvironment = new();

    public ProgramNode Apply(ProgramNode program)
    {
        _program = program;
        _typeRefParser = new TypeRefParser(program);
        _resolver = new ExpressionTypeResolver(program);
        var globalVariables = InferGlobalVariables(program.GlobalVariables);
        var programWithGlobals = program with { GlobalVariables = globalVariables };
        _program = programWithGlobals;
        _typeRefParser = new TypeRefParser(programWithGlobals);
        _resolver = new ExpressionTypeResolver(programWithGlobals);
        _typeSystem = new TypeSystem(programWithGlobals);
        _globalTypeEnvironment = BuildGlobalTypeEnvironment(globalVariables);

        return programWithGlobals with
        {
            Functions = program.Functions.Select(InferFunction).ToList(),
            Structs = program.Structs.Select(structNode => structNode with
            {
                Methods = structNode.Methods.Select(InferFunction).ToList(),
            }).ToList(),
            TaggedUnions = program.TaggedUnions.Select(union => union with
            {
                Methods = union.Methods.Select(InferFunction).ToList(),
            }).ToList(),
        };
    }

    private IReadOnlyList<GlobalVariableNode> InferGlobalVariables(IReadOnlyList<GlobalVariableNode> globals)
    {
        var typeEnvironment = BuildGlobalTypeEnvironment(globals);
        var inferred = new List<GlobalVariableNode>();

        foreach (var global in globals)
        {
            var declaredTypeRef = TypeRefOrNull(global.TypeNode);
            var initializer = InferExpression(
                global.Initializer,
                typeEnvironment,
                declaredTypeRef);
            var type = InferVariableTypeRef(
                global.Location,
                global.Name,
                declaredTypeRef,
                initializer,
                typeEnvironment,
                "global");

            if (type is not null)
            {
                SetVariableType(typeEnvironment, global.Name, type);
            }

            inferred.Add(global with
            {
                TypeNode = CreateInferredTypeNode(global.Location, type),
                Initializer = initializer,
            });
        }

        return inferred;
    }

    private TypeEnvironment BuildGlobalTypeEnvironment(IEnumerable<GlobalVariableNode> globals)
    {
        var typeEnvironment = new TypeEnvironment();
        foreach (var global in globals)
        {
            var type = TypeRefOrUnknown(global.TypeNode);
            if (type is not TypeRef.Unknown)
            {
                typeEnvironment.Set(global.Name, type);
            }
        }

        return typeEnvironment;
    }

    private FunctionNode InferFunction(FunctionNode function)
    {
        var typeEnvironment = _globalTypeEnvironment.Clone();
        AddImplicitSelfBinding(function, typeEnvironment);
        var selfType = GetSelfSubstitutionType(function);
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            SetVariableType(typeEnvironment, parameter.Name, SubstituteSelf(TypeRefOrUnknown(parameter.TypeNode), selfType));
        }

        return function with
        {
            Body = InferStatements(function.Body, typeEnvironment, TypeRefOrUnknown(function.ReturnTypeNode)),
        };
    }

    private IReadOnlyList<StatementNode> InferStatements(
        IReadOnlyList<StatementNode> statements,
        TypeEnvironment typeEnvironment,
        TypeRef? functionReturnType = null)
    {
        var inferred = new List<StatementNode>();
        foreach (var statement in statements)
        {
            inferred.Add(InferStatement(statement, typeEnvironment, functionReturnType));
        }

        return inferred;
    }

    private StatementNode InferStatement(
        StatementNode statement,
        TypeEnvironment typeEnvironment,
        TypeRef? functionReturnType = null) => statement switch
    {
        LetStatement let => InferLetStatement(let, typeEnvironment),
        ReturnStatement ret => ret with { Expression = InferExpression(ret.Expression, typeEnvironment, functionReturnType) },
        CStatement c => c with { Expression = InferExpression(c.Expression, typeEnvironment)! },
        IfStatement ifStatement => ifStatement with
        {
            Condition = InferExpression(ifStatement.Condition, typeEnvironment)!,
            ThenBody = InferStatements(ifStatement.ThenBody, typeEnvironment.Clone(), functionReturnType),
            ElseBranch = ifStatement.ElseBranch is null
                ? null
                : InferStatement(ifStatement.ElseBranch, typeEnvironment.Clone(), functionReturnType),
        },
        ElseBlockStatement elseBlock => elseBlock with
        {
            Body = InferStatements(elseBlock.Body, typeEnvironment.Clone(), functionReturnType),
        },
        WhileStatement whileStatement => whileStatement with
        {
            Condition = InferExpression(whileStatement.Condition, typeEnvironment)!,
            Body = InferStatements(whileStatement.Body, typeEnvironment.Clone(), functionReturnType),
        },
        ForStatement forStatement => InferForStatement(forStatement, typeEnvironment, functionReturnType),
        ForeachStatement foreachStatement => InferForeachStatement(foreachStatement, typeEnvironment, functionReturnType),
        SwitchStatement switchStatement => switchStatement with
        {
            Expression = InferExpression(switchStatement.Expression, typeEnvironment)!,
            Cases = switchStatement.Cases.Select(switchCase => switchCase with
            {
                Pattern = InferExpression(switchCase.Pattern, typeEnvironment)!,
                Body = InferStatements(switchCase.Body, typeEnvironment.Clone(), functionReturnType),
            }).ToList(),
            DefaultBody = InferStatements(switchStatement.DefaultBody, typeEnvironment.Clone(), functionReturnType),
        },
        MatchStatement matchStatement => matchStatement with
        {
            Expression = InferExpression(matchStatement.Expression, typeEnvironment)!,
            Arms = matchStatement.Arms.Select(arm => arm with
            {
                Body = InferStatements(arm.Body, typeEnvironment.Clone(), functionReturnType),
            }).ToList(),
        },
        _ => statement,
    };

    private LetStatement InferLetStatement(
        LetStatement let,
        TypeEnvironment typeEnvironment)
    {
        var declaredTypeRef = TypeRefOrNull(let.TypeNode);
        var initializer = InferExpression(
            let.Initializer,
            typeEnvironment,
            declaredTypeRef);
        var type = InferVariableTypeRef(let.Location, let.Name, declaredTypeRef, initializer, typeEnvironment, "local");
        if (type is not null)
        {
            SetVariableType(typeEnvironment, let.Name, type);
        }

        return let with
        {
            TypeNode = CreateInferredTypeNode(let.Location, type),
            Initializer = initializer,
        };
    }

    private ForStatement InferForStatement(
        ForStatement forStatement,
        TypeEnvironment typeEnvironment,
        TypeRef? functionReturnType)
    {
        var forTypeEnvironment = typeEnvironment.Clone();
        var cachedRangeEndInitializer = InferOptionalForDeclarationInitializer(forStatement.CachedRangeEndInitializer, forTypeEnvironment);
        var counterInitializer = InferOptionalForDeclarationInitializer(forStatement.CounterInitializer, forTypeEnvironment);
        var initializer = InferForInitializer(forStatement.Initializer, forTypeEnvironment);
        return forStatement with
        {
            CachedRangeEndInitializer = cachedRangeEndInitializer,
            CounterInitializer = counterInitializer,
            Initializer = initializer,
            Condition = InferExpression(forStatement.Condition, forTypeEnvironment)!,
            Increment = InferExpression(forStatement.Increment, forTypeEnvironment)!,
            CounterIncrement = InferExpression(forStatement.CounterIncrement, forTypeEnvironment),
            Body = InferStatements(forStatement.Body, forTypeEnvironment, functionReturnType),
        };
    }

    private ForInitializerNode InferForInitializer(
        ForInitializerNode initializer,
        TypeEnvironment typeEnvironment) => initializer switch
    {
        ForDeclarationInitializerNode declaration => InferForDeclarationInitializer(declaration, typeEnvironment),
        ForExpressionInitializerNode expression => expression with
        {
            Expression = InferExpression(expression.Expression, typeEnvironment)!,
        },
        _ => initializer,
    };

    private ForDeclarationInitializerNode InferForDeclarationInitializer(
        ForDeclarationInitializerNode declaration,
        TypeEnvironment typeEnvironment)
    {
        var declaredTypeRef = TypeRefOrNull(declaration.TypeNode);
        var initializer = InferExpression(
            declaration.Initializer,
            typeEnvironment,
            declaredTypeRef);
        var type = InferVariableTypeRef(
            declaration.Location,
            declaration.Name,
            declaredTypeRef,
            initializer,
            typeEnvironment,
            "for variable");
        if (type is not null)
        {
            SetVariableType(typeEnvironment, declaration.Name, type);
        }

        return declaration with
        {
            TypeNode = CreateInferredTypeNode(declaration.Location, type),
            Initializer = initializer,
        };
    }

    private ForDeclarationInitializerNode? InferOptionalForDeclarationInitializer(
        ForDeclarationInitializerNode? declaration,
        TypeEnvironment typeEnvironment) =>
        declaration is null ? null : InferForDeclarationInitializer(declaration, typeEnvironment);

    private static TypeNode? CreateInferredTypeNode(Location location, TypeRef? type)
    {
        if (type is null or TypeRef.Unknown)
        {
            return null;
        }

        var typeNode = TypeNode.CreateFromText(location, TypeRefFormatter.ToCxString(type));
        typeNode.Semantic.Type = type;
        return typeNode;
    }

    private static TypeNode? PreserveTypeNode(TypeNode? typeNode) =>
        typeNode is null
            ? null
            : SyntaxNode.CloneSemantic(typeNode, typeNode with { });

    private ForeachStatement InferForeachStatement(
        ForeachStatement foreachStatement,
        TypeEnvironment typeEnvironment,
        TypeRef? functionReturnType)
    {
        var iterableExpression = InferExpression(foreachStatement.IterableExpression, typeEnvironment);
        var foreachTypeEnvironment = typeEnvironment.Clone();
        var iterableTypeRef = _resolver?.ResolveTypeRef(iterableExpression, typeEnvironment);
        TypeRef? elementType = null;
        TypeRef? keyType = null;
        if (iterableExpression is ScalarRangeExpressionNode && iterableTypeRef is not null)
        {
            elementType = iterableTypeRef;
            AddForeachBindings(foreachStatement, foreachTypeEnvironment, elementType);
        }
        else if (iterableTypeRef is not null
            && TryResolveForeachTypes(foreachStatement, iterableTypeRef, out elementType, out keyType))
        {
            if (foreachStatement.KeyBinding is { } keyBinding && keyType is not null)
            {
                SetVariableType(
                    foreachTypeEnvironment,
                    keyBinding.Name,
                    TypeRefOrNull(keyBinding.TypeNode) ?? keyType);
            }

            AddForeachBindings(foreachStatement, foreachTypeEnvironment, elementType);
        }

        var typedForeachStatement = elementType is null
            ? foreachStatement
            : ApplyForeachBindingTypes(foreachStatement, elementType, keyType);

        return foreachStatement with
        {
            IterableExpression = iterableExpression!,
            IndexBinding = typedForeachStatement.IndexBinding,
            KeyBinding = typedForeachStatement.KeyBinding,
            ValueBinding = typedForeachStatement.ValueBinding,
            Body = InferStatements(foreachStatement.Body, foreachTypeEnvironment, functionReturnType),
        };
    }

    private ForeachStatement ApplyForeachBindingTypes(
        ForeachStatement foreachStatement,
        TypeRef elementType,
        TypeRef? keyType)
    {
        return foreachStatement with
        {
            IndexBinding = foreachStatement.IndexBinding is null
                ? null
                : FillBindingType(foreachStatement.IndexBinding, TypeRef.Usize),
            KeyBinding = foreachStatement.KeyBinding is null || keyType is null
                ? foreachStatement.KeyBinding
                : FillBindingType(foreachStatement.KeyBinding, keyType),
            ValueBinding = FillBindingType(foreachStatement.ValueBinding, elementType),
        };
    }

    private ForeachBinding FillBindingType(ForeachBinding binding, TypeRef inferredType) =>
        TypeRefOrNull(binding.TypeNode) is null
            ? binding with { TypeNode = CreateInferredTypeNode(binding.Location, inferredType) }
            : binding;

    private void AddForeachBindings(
        ForeachStatement foreachStatement,
        TypeEnvironment typeEnvironment,
        TypeRef elementType)
    {
        if (foreachStatement.IndexBinding is { } indexBinding)
        {
            var indexBindingType = TypeRefOrNull(indexBinding.TypeNode);
            SetVariableType(typeEnvironment, indexBinding.Name, indexBindingType ?? TypeRef.Usize);
        }

        var valueBindingType = TypeRefOrNull(foreachStatement.ValueBinding.TypeNode);
        SetVariableType(typeEnvironment, foreachStatement.ValueBinding.Name, valueBindingType ?? elementType);
    }

    private bool TryResolveForeachTypes(
        ForeachStatement foreachStatement,
        TypeRef iterableType,
        out TypeRef elementType,
        out TypeRef? keyType)
    {
        elementType = new TypeRef.Unknown();
        keyType = null;

        return _typeSystem?.TryResolveForeachTypes(
            iterableType,
            keyValue: foreachStatement.KeyBinding is not null,
            out elementType,
            out keyType) == true;
    }

    private TypeRef? InferVariableTypeRef(
        Location location,
        string name,
        TypeRef? declaredType,
        ExpressionNode? initializer,
        TypeEnvironment typeEnvironment,
        string subject)
    {
        if (declaredType is not null and not TypeRef.Unknown)
        {
            return declaredType;
        }

        if (initializer is null)
        {
            diagnostics.Report(location, $"Cannot infer type for {subject} '{name}' without an initializer.");
            return declaredType;
        }

        if (initializer is LiteralExpressionNode { LiteralText: "null" })
        {
            diagnostics.Report(location, $"Cannot infer type for {subject} '{name}' from null; write an explicit pointer type.");
            return declaredType;
        }

        if (initializer is InitializerExpressionNode { TypeNameNode: null })
        {
            diagnostics.Report(location, $"Cannot infer type for {subject} '{name}' from an untyped initializer; write an explicit type.");
            return declaredType;
        }

        var inferredType = _resolver?.ResolveTypeRef(initializer, typeEnvironment);
        if (inferredType is null or TypeRef.Unknown or TypeRef.Null)
        {
            diagnostics.Report(
                location,
                BuildUnknownExpressionTypeDiagnostic(subject, name, initializer, typeEnvironment));
            return declaredType;
        }

        return inferredType;
    }

    private string BuildUnknownExpressionTypeDiagnostic(
        string subject,
        string name,
        ExpressionNode initializer,
        TypeEnvironment typeEnvironment)
    {
        if (TryBuildGenericInferenceDiagnostic(subject, name, initializer, typeEnvironment) is { } diagnostic)
        {
            return diagnostic;
        }

        return $"Cannot infer type for {subject} '{name}'; expression type is unknown.";
    }

    private string? TryBuildGenericInferenceDiagnostic(
        string subject,
        string name,
        ExpressionNode initializer,
        TypeEnvironment typeEnvironment)
    {
        if (_program is null || _resolver is null)
        {
            return null;
        }

        initializer = UnwrapParentheses(initializer);
        if (initializer is not CallExpressionNode call)
        {
            return null;
        }

        if (call.Callee is NameExpressionNode functionName)
        {
            var function = _program.Functions.FirstOrDefault(function =>
                OwnerTypeName(function) is null
                && function.Name == functionName.Name
                && function.TypeParameters.Count > 0);
            if (function is null
                || _resolver.InferFunctionTypeArgumentRefs(function.TypeParameters, function.Parameters, call.Arguments, typeEnvironment, skipSelf: false) is not null)
            {
                return null;
            }

            return BuildGenericCallDiagnostic(subject, name, function, function.Name, function.Name, call.Arguments, skipSelf: false);
        }

        if (call.Callee is not MemberExpressionNode member || ExpressionNameFacts.GetQualifiedName(member.Target) is not { } targetName)
        {
            return null;
        }

        if (!typeEnvironment.Types.ContainsKey(targetName))
        {
            var staticFunction = _program.Functions.FirstOrDefault(function =>
                function.IsStatic
                && OwnerTypeName(function) == targetName
                && function.Name == member.MemberName
                && function.TypeParameters.Count > 0);
            if (staticFunction is null
                || _resolver.InferFunctionTypeArgumentRefs(staticFunction.TypeParameters, staticFunction.Parameters, call.Arguments, typeEnvironment, skipSelf: false) is not null)
            {
                return null;
            }

            return BuildGenericCallDiagnostic(
                subject,
                name,
                staticFunction,
                $"{targetName}.{member.MemberName}",
                $"{targetName}<int>.{member.MemberName}",
                call.Arguments,
                skipSelf: false);
        }

        return null;
    }

    private string BuildGenericCallDiagnostic(
        string subject,
        string variableName,
        FunctionNode function,
        string callName,
        string suggestedCall,
        IReadOnlyList<ExpressionNode> arguments,
        bool skipSelf)
    {
        var fixedParameters = function.Parameters
            .Skip(skipSelf ? 1 : 0)
            .Where(parameter => !parameter.IsVariadic)
            .ToList();
        var unbound = function.TypeParameters
            .Where(typeParameter =>
                !fixedParameters.Any(parameter => TypeMentionsParameter(TypeRefOrUnknown(parameter.TypeNode), typeParameter))
                && TypeMentionsParameter(TypeRefOrUnknown(function.ReturnTypeNode), typeParameter))
            .ToList();

        if (unbound.Count == 0)
        {
            return $"Cannot infer type for {subject} '{variableName}'; generic type arguments for '{callName}' could not be inferred from arguments.";
        }

        var parameterText = string.Join(", ", unbound.Select(parameter => $"'{parameter}'"));
        var plural = unbound.Count == 1 ? "parameter" : "parameters";
        var pronoun = unbound.Count == 1 ? "it" : "they";
        var appears = unbound.Count == 1 ? "appears" : "appear";
        var argumentText = arguments.Count == 0 ? "no arguments" : "the provided arguments";
        var suggestion = function.IsStatic && OwnerTypeName(function) is not null
            ? $" Try '{suggestedCall}(...)' and replace 'int' with the desired type."
            : $" Try '{suggestedCall}<int>(...)' and replace 'int' with the desired type.";

        return $"Cannot infer type for {subject} '{variableName}'; generic type {plural} {parameterText} for '{callName}' cannot be inferred from {argumentText} because {pronoun} only {appears} in the return type.{suggestion}";
    }

    private static bool TypeMentionsParameter(TypeRef type, string typeParameter) =>
        TypeRefFacts.UnwrapAlias(type) switch
        {
            TypeRef.Named named => string.Equals(named.Name, typeParameter, StringComparison.Ordinal)
                || named.Arguments.Any(argument => TypeMentionsParameter(argument, typeParameter)),
            TypeRef.Pointer pointer => TypeMentionsParameter(pointer.Element, typeParameter),
            TypeRef.FixedArray fixedArray => TypeMentionsParameter(fixedArray.Element, typeParameter),
            TypeRef.Function function => function.Parameters.Any(parameter => TypeMentionsParameter(parameter, typeParameter))
                || TypeMentionsParameter(function.ReturnType, typeParameter),
            _ => false,
        };

    private static ExpressionNode UnwrapParentheses(ExpressionNode expression) =>
        expression is ParenthesizedExpressionNode parenthesized
            ? UnwrapParentheses(parenthesized.Expression)
            : expression;

    private ExpressionNode? InferExpression(
        ExpressionNode? expression,
        TypeEnvironment typeEnvironment,
        TypeRef? expectedType = null)
    {
        if (expression is null)
        {
            return null;
        }

        var inferred = expression switch
        {
            ParenthesizedExpressionNode parenthesized => parenthesized with
            {
                Expression = InferExpression(parenthesized.Expression, typeEnvironment, expectedType)!,
            },
            CastExpressionNode cast => cast with
            {
                Expression = InferExpression(cast.Expression, typeEnvironment)!,
            },
            UnaryExpressionNode unary => unary with
            {
                Operand = InferExpression(unary.Operand, typeEnvironment)!,
            },
            PostfixExpressionNode postfix => postfix with
            {
                Operand = InferExpression(postfix.Operand, typeEnvironment)!,
            },
            SizeOfExpressionNode sizeOf => sizeOf with
            {
                ExpressionOperand = InferExpression(sizeOf.ExpressionOperand, typeEnvironment),
            },
            BinaryExpressionNode binary => binary with
            {
                Left = InferExpression(binary.Left, typeEnvironment)!,
                Right = InferExpression(binary.Right, typeEnvironment)!,
            },
            ScalarRangeExpressionNode range => range with
            {
                Start = InferExpression(range.Start, typeEnvironment)!,
                End = InferExpression(range.End, typeEnvironment)!,
            },
            ConditionalExpressionNode conditional => conditional with
            {
                Condition = InferExpression(conditional.Condition, typeEnvironment)!,
                WhenTrue = InferExpression(conditional.WhenTrue, typeEnvironment, expectedType)!,
                WhenFalse = InferExpression(conditional.WhenFalse, typeEnvironment, expectedType)!,
            },
            InitializerExpressionNode initializer => initializer with
            {
                TypeNameNode = PreserveTypeNode(initializer.TypeNameNode),
                Fields = initializer.Fields.Select(field => field with
                {
                    Value = InferExpression(field.Value, typeEnvironment)!,
                }).ToList(),
                Values = initializer.Values
                    .Select(value => InferExpression(value, typeEnvironment)!)
                    .ToList(),
            },
            FunctionExpressionNode function => InferFunctionExpression(function, typeEnvironment, expectedType),
            AssignmentExpressionNode assignment => assignment with
            {
                Target = InferExpression(assignment.Target, typeEnvironment)!,
                Value = InferExpression(assignment.Value, typeEnvironment)!,
            },
            CallExpressionNode call => call with
            {
                Callee = InferExpression(call.Callee, typeEnvironment)!,
                Arguments = call.Arguments
                    .Select(argument => InferExpression(argument, typeEnvironment)!)
                    .ToList(),
            },
            GenericCallExpressionNode call => call with
            {
                Callee = InferExpression(call.Callee, typeEnvironment)!,
                Arguments = call.Arguments
                    .Select(argument => InferExpression(argument, typeEnvironment)!)
                    .ToList(),
            },
            MemberExpressionNode member => member with
            {
                Target = InferExpression(member.Target, typeEnvironment)!,
            },
            IndexExpressionNode index => index with
            {
                Target = InferExpression(index.Target, typeEnvironment)!,
                Index = InferExpression(index.Index, typeEnvironment)!,
            },
            _ => expression,
        };

        inferred.Semantic.Type = _resolver?.ResolveTypeRef(inferred, typeEnvironment) ?? expectedType;
        return inferred;
    }

    private FunctionExpressionNode InferFunctionExpression(
        FunctionExpressionNode function,
        TypeEnvironment typeEnvironment,
        TypeRef? expectedType)
    {
        var inferredReturnType = function.ReturnTypeNode is null && expectedType is TypeRef.Function expectedFunction
            ? expectedFunction.ReturnType
            : null;
        var returnTypeNode = function.ReturnTypeNode ?? CreateInferredTypeNode(function.Location, inferredReturnType);
        var functionReturnType = TypeRefOrUnknown(returnTypeNode);
        var functionTypeEnvironment = typeEnvironment.Clone();
        foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
        {
            SetVariableType(functionTypeEnvironment, parameter.Name, TypeRefOrUnknown(parameter.TypeNode));
        }

        return function with
        {
            ReturnTypeNode = returnTypeNode,
            ExpressionBody = InferExpression(function.ExpressionBody, functionTypeEnvironment, functionReturnType),
            BlockBody = function.BlockBody is null
                ? null
                : InferStatements(function.BlockBody, functionTypeEnvironment, functionReturnType),
        };
    }

    private string? OwnerTypeName(FunctionNode function) =>
        TypeRefOrNull(function.OwnerTypeNode) is TypeRef.Named named ? named.Name : null;

    private void AddImplicitSelfBinding(FunctionNode function, TypeEnvironment typeEnvironment)
    {
        if (function.IsStatic
            || function.OwnerTypeNode is null
            || function.Parameters.Any(parameter => string.Equals(parameter.Name, "self", StringComparison.Ordinal)))
        {
            return;
        }

        var ownerType = TypeRefOrUnknown(function.OwnerTypeNode);
        if (ownerType is not TypeRef.Unknown)
        {
            SetVariableType(typeEnvironment, "self", new TypeRef.Pointer(ownerType));
        }
    }

    private TypeRef? GetSelfSubstitutionType(FunctionNode function)
    {
        if (function.OwnerTypeNode is null)
        {
            return null;
        }

        var ownerType = TypeRefOrUnknown(function.OwnerTypeNode);
        if (ownerType is TypeRef.Named { Arguments.Count: 0 } named
            && GetOwnerTypeParameters(named.Name) is { Count: > 0 } typeParameters)
        {
            return named with
            {
                Arguments = typeParameters
                    .Select(parameter => new TypeRef.Named(parameter, []))
                    .ToList(),
            };
        }

        return ownerType is TypeRef.Unknown ? null : ownerType;
    }

    private IReadOnlyList<string> GetOwnerTypeParameters(string ownerName)
    {
        if (_program is null)
        {
            return [];
        }

        return _program.Structs.FirstOrDefault(structNode => structNode.Name == ownerName)?.TypeParameters
            ?? _program.TypeAdapters.FirstOrDefault(adapter => adapter.Name == ownerName)?.TypeParameters
            ?? [];
    }

    private static TypeRef SubstituteSelf(TypeRef type, TypeRef? selfType) =>
        selfType is null ? type : TypeRefRewriter.SubstituteSelf(type, selfType);

    private TypeRef? TypeRefOrNull(TypeNode? typeNode)
    {
        var type = TypeRefOrUnknown(typeNode);
        return type is TypeRef.Unknown ? null : type;
    }

    private TypeRef TypeRefOrUnknown(TypeNode? typeNode) =>
        typeNode.ToTypeRef(RequireTypeRefParser());

    private TypeRefParser RequireTypeRefParser() =>
        _typeRefParser ?? throw new InvalidOperationException("Type inference has no TypeRef parser.");

    private static void SetVariableType(
        TypeEnvironment typeEnvironment,
        string name,
        TypeRef type)
    {
        typeEnvironment.Set(name, type);
    }

}
