using Cx.Compiler.C;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

internal static class CDeclarationUsageAnalyzer
{
    public static IEnumerable<CDeclareNode> GetDeclarationsToInclude(ProgramNode program)
    {
        var usage = GetCDeclarationUsage(program);
        foreach (var declaration in program.CDeclarations)
        {
            if (IsCDeclarationUsed(declaration, usage))
            {
                yield return declaration;
            }
        }
    }

    private sealed record CDeclarationUsage(
        IReadOnlySet<string> Functions,
        IReadOnlySet<string> Types,
        IReadOnlySet<string> Values);

    private static CDeclarationUsage GetCDeclarationUsage(ProgramNode program)
    {
        var declaredFunctions = program.CDeclarations
            .SelectMany(declaration => declaration.Functions)
            .Select(function => function.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var declaredTypes = program.CDeclarations
            .SelectMany(GetCDeclarationTypeNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var declaredValues = program.CDeclarations
            .SelectMany(GetCDeclarationValueNames)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        var functions = EnumerateExpressionNodes(program)
            .Select(GetCalledFunctionName)
            .Where(name => name is not null && declaredFunctions.Contains(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
        var types = EnumerateTypeReferences(program)
            .SelectMany(ExtractTypeNames)
            .Where(declaredTypes.Contains)
            .ToHashSet(StringComparer.Ordinal);
        var values = EnumerateExpressionNodes(program)
            .Select(GetValueReferenceName)
            .Where(name => name is not null && declaredValues.Contains(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        return new CDeclarationUsage(functions, types, values);
    }

    private static string? GetCalledFunctionName(ExpressionNode expression) => expression switch
    {
        CallExpressionNode call => ExpressionNameFacts.GetQualifiedName(call.Callee),
        GenericCallExpressionNode call => ExpressionNameFacts.GetQualifiedName(call.Callee),
        _ => null,
    };

    private static string? GetValueReferenceName(ExpressionNode expression) => expression switch
    {
        NameExpressionNode name => name.Name,
        MemberExpressionNode member => ExpressionNameFacts.GetQualifiedName(member),
        _ => null,
    };

    private static IEnumerable<string> GetCDeclarationTypeNames(CDeclareNode declaration) =>
        declaration.TypeAliases.Select(typeAlias => typeAlias.Name)
            .Concat(declaration.Structs.Select(structNode => structNode.Name))
            .Concat(declaration.Enums.Select(enumNode => enumNode.Name))
            .Concat(declaration.Unions.Select(union => union.Name));

    private static IEnumerable<string> GetCDeclarationValueNames(CDeclareNode declaration) =>
        declaration.Constants.Select(constant => constant.Name)
            .Concat(declaration.Enums.SelectMany(enumNode => enumNode.Members.Select(member => member.Name)));

    private static IEnumerable<ExpressionNode> EnumerateExpressionNodes(ProgramNode program)
    {
        foreach (var global in program.GlobalVariables.Where(global => !global.IsHeaderDeclaration))
        {
            if (global.Initializer is null)
            {
                continue;
            }

            foreach (var expression in CExpressionTraversal.EnumerateExpressionNodes(global.Initializer))
            {
                yield return expression;
            }
        }

        foreach (var function in program.Functions)
        {
            foreach (var expression in CExpressionTraversal.EnumerateExpressionNodes(function.Body))
            {
                yield return expression;
            }
        }
    }

    private static IEnumerable<TypeRef> EnumerateTypeReferences(ProgramNode program)
    {
        foreach (var global in program.GlobalVariables.Where(global => !global.IsHeaderDeclaration))
        {
            yield return ResolveDeclarationType(global.TypeNode, global.Name);
            foreach (var type in EnumerateExpressionTypeReferences(global.Initializer))
            {
                yield return type;
            }
        }

        foreach (var typeAlias in program.TypeAliases.Where(typeAlias => !typeAlias.IsHeaderDeclaration))
        {
            yield return ResolveTypeAliasType(typeAlias);
        }

        foreach (var function in program.Functions)
        {
            yield return ResolveDeclarationType(function.ReturnTypeNode, "return");
            foreach (var parameter in function.Parameters.Where(parameter => !parameter.IsVariadic))
            {
                yield return ResolveDeclarationType(parameter.TypeNode, parameter.Name);
            }

            foreach (var type in EnumerateStatementTypeReferences(function.Body))
            {
                yield return type;
            }
        }

        foreach (var structNode in program.Structs.Where(structNode => !structNode.IsHeaderDeclaration))
        {
            foreach (var field in structNode.Fields)
            {
                yield return ResolveDeclarationType(field.TypeNode, field.Name);
            }
        }

        foreach (var taggedUnion in program.TaggedUnions.Where(taggedUnion => !taggedUnion.IsHeaderDeclaration))
        {
            foreach (var variant in taggedUnion.Variants)
            {
                yield return ResolveDeclarationType(variant.TypeNode, variant.Name);
            }
        }
    }

    private static IEnumerable<TypeRef> EnumerateStatementTypeReferences(IEnumerable<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case LetStatement letStatement:
                    yield return ResolveDeclarationType(letStatement.TypeNode, letStatement.Name);
                    foreach (var type in EnumerateExpressionTypeReferences(letStatement.Initializer))
                    {
                        yield return type;
                    }
                    break;
                case ReturnStatement returnStatement:
                    foreach (var type in EnumerateExpressionTypeReferences(returnStatement.Expression))
                    {
                        yield return type;
                    }
                    break;
                case CStatement cStatement:
                    foreach (var type in EnumerateExpressionTypeReferences(cStatement.Expression))
                    {
                        yield return type;
                    }
                    break;
                case IfStatement ifStatement:
                    foreach (var type in EnumerateExpressionTypeReferences(ifStatement.Condition)
                        .Concat(EnumerateStatementTypeReferences(ifStatement.ThenBody)))
                    {
                        yield return type;
                    }
                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (var type in EnumerateStatementTypeReferences([ifStatement.ElseBranch]))
                        {
                            yield return type;
                        }
                    }
                    break;
                case ElseBlockStatement elseBlock:
                    foreach (var type in EnumerateStatementTypeReferences(elseBlock.Body))
                    {
                        yield return type;
                    }
                    break;
                case WhileStatement whileStatement:
                    foreach (var type in EnumerateExpressionTypeReferences(whileStatement.Condition)
                        .Concat(EnumerateStatementTypeReferences(whileStatement.Body)))
                    {
                        yield return type;
                    }
                    break;
                case ForStatement forStatement:
                    foreach (var type in EnumerateForInitializerTypeReferences(forStatement.CachedRangeEndInitializer)
                        .Concat(EnumerateForInitializerTypeReferences(forStatement.CounterInitializer))
                        .Concat(EnumerateForInitializerTypeReferences(forStatement.Initializer))
                        .Concat(EnumerateExpressionTypeReferences(forStatement.Condition))
                        .Concat(EnumerateExpressionTypeReferences(forStatement.Increment))
                        .Concat(EnumerateExpressionTypeReferences(forStatement.CounterIncrement))
                        .Concat(EnumerateStatementTypeReferences(forStatement.Body)))
                    {
                        yield return type;
                    }
                    break;
                case SwitchStatement switchStatement:
                    foreach (var type in EnumerateExpressionTypeReferences(switchStatement.Expression))
                    {
                        yield return type;
                    }
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        foreach (var type in EnumerateExpressionTypeReferences(switchCase.Pattern)
                            .Concat(EnumerateStatementTypeReferences(switchCase.Body)))
                        {
                            yield return type;
                        }
                    }
                    foreach (var type in EnumerateStatementTypeReferences(switchStatement.DefaultBody))
                    {
                        yield return type;
                    }
                    break;
            }
        }
    }

    private static IEnumerable<TypeRef> EnumerateForInitializerTypeReferences(ForInitializerNode? initializer) => initializer switch
    {
        ForDeclarationInitializerNode declaration => [ResolveDeclarationType(declaration.TypeNode, declaration.Name), .. EnumerateExpressionTypeReferences(declaration.Initializer)],
        ForExpressionInitializerNode expression => EnumerateExpressionTypeReferences(expression.Expression),
        _ => [],
    };

    private static IEnumerable<TypeRef> EnumerateExpressionTypeReferences(ExpressionNode? expression)
    {
        if (expression is null)
        {
            yield break;
        }

        foreach (var node in CExpressionTraversal.EnumerateExpressionNodes(expression))
        {
            switch (node)
            {
                case CastExpressionNode cast:
                    yield return ResolveTypeExpression(cast.TargetTypeNode);
                    break;
                case InitializerExpressionNode { TypeNameNode: not null } initializer:
                    yield return ResolveTypeExpression(initializer.TypeNameNode);
                    break;
                case SizeOfExpressionNode { TypeOperandNode: not null } sizeOf:
                    yield return ResolveTypeExpression(sizeOf.TypeOperandNode);
                    break;
            }
        }
    }

    private static IEnumerable<string> ExtractTypeNames(TypeRef type)
    {
        type = TypeRefFacts.UnwrapAlias(type);
        switch (type)
        {
            case TypeRef.Named named:
                if (!IsTypeReferenceKeyword(named.Name))
                {
                    yield return named.Name;
                }
                foreach (var argumentName in named.Arguments.SelectMany(ExtractTypeNames))
                {
                    yield return argumentName;
                }
                break;
            case TypeRef.Pointer pointer:
                foreach (var name in ExtractTypeNames(pointer.Element))
                {
                    yield return name;
                }
                break;
            case TypeRef.FixedArray fixedArray:
                foreach (var name in ExtractTypeNames(fixedArray.Element))
                {
                    yield return name;
                }
                break;
            case TypeRef.Function function:
                foreach (var name in function.Parameters.SelectMany(ExtractTypeNames))
                {
                    yield return name;
                }
                foreach (var name in ExtractTypeNames(function.ReturnType))
                {
                    yield return name;
                }
                break;
        }
    }

    private static TypeRef ResolveTypeAliasType(TypeAliasNode typeAlias) =>
        typeAlias.TargetTypeNode?.Semantic.Type is { } type && type is not TypeRef.Unknown
            ? type
            : throw CEmissionGuards.UnresolvedTypeAlias(typeAlias);

    private static TypeRef ResolveDeclarationType(TypeNode? typeNode, string name) =>
        CDeclarationLowerer.ResolveDeclarationType(typeNode, name);

    private static TypeRef ResolveTypeExpression(TypeNode? typeNode) =>
        typeNode?.Semantic.Type is { } type && type is not TypeRef.Unknown
            ? type
            : throw CEmissionGuards.UnresolvedTypeExpression(typeNode);

    private static bool IsTypeReferenceKeyword(string name) =>
        name is
            "const" or
            "fn" or
            "opaque" or
            "void" or
            "char" or
            "short" or
            "int" or
            "long" or
            "float" or
            "double" or
            "bool" or
            "usize" or
            "i8" or
            "i16" or
            "i32" or
            "i64" or
            "u8" or
            "u16" or
            "u32" or
            "u64";

    private static bool IsCDeclarationUsed(CDeclareNode declaration, CDeclarationUsage usage) =>
        declaration.Functions.Any(function => usage.Functions.Contains(function.Name))
        || GetCDeclarationTypeNames(declaration).Any(usage.Types.Contains)
        || GetCDeclarationValueNames(declaration).Any(usage.Values.Contains);
}
