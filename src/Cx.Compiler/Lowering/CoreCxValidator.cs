using Cx.Compiler.Diagnostics;
using Cx.Compiler.Semantic;
using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Validates the CX subset accepted by target backends after semantic lowering.
/// This is the phase boundary between resolved CX and Core CX.
/// </summary>
internal sealed class CoreCxValidator(DiagnosticBag diagnostics)
{
    private IReadOnlyDictionary<string, InterfaceNode> _interfaces =
        new Dictionary<string, InterfaceNode>(StringComparer.Ordinal);
    private IReadOnlySet<string> _taggedUnionNames =
        new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlySet<string> _enumNames =
        new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlySet<string> _moduleQualifiers =
        new HashSet<string>(StringComparer.Ordinal);

    public void Validate(ProgramNode program)
    {
        program.Semantic.IsCoreCxValidated = false;
        _interfaces = program.Interfaces.ToDictionary(
            interfaceNode => interfaceNode.Name,
            StringComparer.Ordinal);
        _taggedUnionNames = program.TaggedUnions
            .Select(union => union.Name)
            .ToHashSet(StringComparer.Ordinal);
        _enumNames = program.Enums
            .Select(enumNode => enumNode.Name)
            .ToHashSet(StringComparer.Ordinal);
        _moduleQualifiers = program.Imports
            .Select(import => import.Alias ?? import.ModuleName)
            .ToHashSet(StringComparer.Ordinal);
        ValidateDataEnums(program);
        ValidateLinkedDeclarations(program);
        var coreFunctions = CoreFunctions(program).ToList();
        foreach (var function in coreFunctions)
        {
            ValidateCoreFunction(function);
        }

        foreach (var node in CompileTimeDeclarationRoots(program)
            .Concat(ExecutableAstTraversal.GetRoots(
                program,
                coreFunctions))
            .SelectMany(AstTraversal.DescendantsAndSelf))
        {
            ReportResidue(node);
        }

        if (!diagnostics.HasErrors)
        {
            program.Semantic.IsCoreCxValidated = true;
        }
    }

    private void ValidateLinkedDeclarations(ProgramNode program)
    {
        foreach (var declaration in program.GlobalVariables
                     .Cast<TopLevelNode>()
                     .Concat(program.ExternFunctions))
        {
            if (declaration.Semantic.CoreSymbol is not null)
            {
                continue;
            }

            var name = declaration switch
            {
                GlobalVariableNode global => global.Name,
                ExternFunctionNode function => function.Name,
                _ => "<unknown>",
            };
            diagnostics.Report(
                declaration.Location,
                $"Invalid Core CX: linked declaration '{name}' has no canonical link name.");
        }
    }

    private void ValidateCoreFunction(FunctionNode function)
    {
        if (function.Semantic.CoreFunction is not { } core)
        {
            diagnostics.Report(
                function.Location,
                $"Invalid Core CX: concrete function '{function.Name}' has no concrete function type facts.");
            return;
        }

        if (function.OwnerTypeNode is not null && core.OwnerType is null)
        {
            diagnostics.Report(
                function.Location,
                $"Invalid Core CX: method '{function.Name}' has no concrete owner type.");
        }

        foreach (var (name, type) in new[]
                 {
                     ("owner", core.OwnerType),
                     ("Self API", core.SelfApiType),
                 })
        {
            if (type is not null && ContainsNonCoreType(type))
            {
                diagnostics.Report(
                    function.Location,
                    $"Invalid Core CX: function '{function.Name}' has non-concrete {name} type " +
                    $"'{TypeRefFormatter.ToCxString(type)}'.");
            }
        }
    }

    private static bool ContainsNonCoreType(TypeRef type) =>
        TypeRefFacts.UnwrapAlias(type) switch
        {
            TypeRef.Unknown => true,
            TypeRef.Named { Name: "Self" } => true,
            TypeRef.Named named => named.Arguments.Any(ContainsNonCoreType),
            TypeRef.Pointer pointer => ContainsNonCoreType(pointer.Element),
            TypeRef.Const constType => ContainsNonCoreType(constType.Element),
            TypeRef.FixedArray array => ContainsNonCoreType(array.Element),
            TypeRef.Function function =>
                function.Parameters.Any(ContainsNonCoreType)
                || ContainsNonCoreType(function.ReturnType),
            _ => false,
        };

    private void ValidateDataEnums(ProgramNode program)
    {
        foreach (var enumNode in program.Enums.Where(node => node.IsDataEnum))
        {
            foreach (var member in enumNode.Members)
            {
                var values = (member.DataValues ?? [])
                    .Select(value => value.Name)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var field in enumNode.DataFields ?? [])
                {
                    if (!values.Contains(field.Name))
                    {
                        diagnostics.Report(
                            member.Location,
                            $"Invalid Core CX: data-enum member '{enumNode.Name}.{member.Name}' " +
                            $"has no materialized value for field '{field.Name}'.");
                    }
                }
            }
        }
    }

    private static IEnumerable<SyntaxNode> CompileTimeDeclarationRoots(
        ProgramNode program) =>
        program.Declarations
            .Where(declaration => declaration is
                CompileTimeScriptDeclarationNode
                or CompileTimeIfTopLevelNode
                or CompileTimeForeachTopLevelNode
                or MacroInvocationDeclarationNode
                or CompileTimeConstantNode
                or FunctionNode { IsCompileTime: true })
            .Cast<SyntaxNode>()
            .Concat(program.CDeclarations
            .SelectMany(declaration => declaration.Members)
            .Where(member => member is
                CompileTimeIfDeclarationNode
                or CompileTimeForeachDeclarationNode))
            .Concat(program.Structs.SelectMany(node => node.CompileTimeMemberNodes))
            .Concat(program.Extensions.SelectMany(node => node.CompileTimeMemberNodes))
            .Concat(program.TypeAdapters.SelectMany(node => node.CompileTimeMemberNodes));

    private static IEnumerable<FunctionNode> CoreFunctions(
        ProgramNode program) =>
        program.Functions.Where(function =>
            function.TypeParameters.Count == 0);

    private void ReportResidue(SyntaxNode node)
    {
        switch (node)
        {
            case TypeNode typeNode
                when typeNode.Semantic.Type is not { } type
                    || ContainsNonCoreType(type):
                diagnostics.Report(
                    typeNode.Location,
                    "Invalid Core CX: runtime type syntax has no concrete TypeRef: " +
                    $"'{TypeSyntaxFormatter.ToCxString(typeNode.Syntax)}'.");
                break;
            case ExpressionNode
                {
                    Semantic.ValueConversion: { } conversion,
                } expression
                when ConversionContainsNonCoreType(conversion):
                diagnostics.Report(
                    expression.Location,
                    "Invalid Core CX: value conversion contains a non-concrete type.");
                break;
            case MemberExpressionNode member
                when IsStaticSourceReference(member)
                    && member.Semantic.MemberReference is null:
                diagnostics.Report(
                    member.Location,
                    $"Invalid Core CX: static member reference '{member.MemberName}' " +
                    "has no canonical reference identity.");
                break;
            case NameExpressionNode
                {
                    Semantic.Symbol:
                    {
                        Kind: SymbolKind.Function,
                    },
                    Semantic.CoreSymbol: null,
                    Semantic.CoreFunctionReference: null,
                } name:
                diagnostics.Report(
                    name.Location,
                    $"Invalid Core CX: function reference '{name.Name}' "
                    + "has no canonical function identity.");
                break;
            case MemberExpressionNode member
                when RequiresCoreMemberAccess(member)
                    && member.Semantic.CoreMemberAccess is null:
                diagnostics.Report(
                    member.Location,
                    $"Invalid Core CX: member access '{member.MemberName}' "
                    + "has no explicit value/pointer access facts.");
                break;
            case CompileTimeIfDeclarationNode conditional:
                diagnostics.Report(
                    conditional.Location,
                    "Internal lowering error: compile-time @if declaration remains after lowering.");
                break;
            case CompileTimeForeachDeclarationNode loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: compile-time @foreach declaration remains after lowering.");
                break;
            case CompileTimeScriptDeclarationNode script:
                diagnostics.Report(
                    script.Location,
                    "Internal lowering error: compile-time declaration script remains after lowering.");
                break;
            case CompileTimeIfTopLevelNode conditional:
                diagnostics.Report(
                    conditional.Location,
                    "Internal lowering error: top-level compile-time @if remains after lowering.");
                break;
            case CompileTimeForeachTopLevelNode loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: top-level compile-time @foreach remains after lowering.");
                break;
            case MacroInvocationDeclarationNode invocation:
                diagnostics.Report(
                    invocation.Location,
                    $"Internal lowering error: declaration macro invocation '{invocation.MacroName}' remains after lowering.");
                break;
            case CompileTimeConstantNode constant:
                diagnostics.Report(
                    constant.Location,
                    $"Internal lowering error: compile-time constant '{constant.Name}' remains after lowering.");
                break;
            case FunctionNode { IsCompileTime: true } function:
                diagnostics.Report(
                    function.Location,
                    $"Internal lowering error: compile-time function '{function.Name}' remains after lowering.");
                break;
            case CompileTimeLetStatementNode binding:
                diagnostics.Report(
                    binding.Location,
                    $"Internal lowering error: compile-time @let binding '{binding.Name}' remains after lowering.");
                break;
            case MacroInvocationStatementNode invocation:
                diagnostics.Report(
                    invocation.Location,
                    $"Internal lowering error: macro invocation '{invocation.MacroName}' remains after lowering.");
                break;
            case CompileTimeIfStatementNode conditional:
                diagnostics.Report(
                    conditional.Location,
                    "Internal lowering error: compile-time @if statement remains after lowering.");
                break;
            case CompileTimeForeachStatementNode loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: compile-time @foreach statement remains after lowering.");
                break;
            case ForeachStatement loop:
                diagnostics.Report(
                    loop.Location,
                    "Internal lowering error: foreach statement remains after post-semantic lowering.");
                break;
            case MatchStatement match:
                diagnostics.Report(
                    match.Location,
                    "Internal lowering error: match statement remains after post-semantic lowering.");
                break;
            case PlaceholderExpressionNode placeholder:
                diagnostics.Report(
                    placeholder.Location,
                    "Internal lowering error: compile-time placeholder remains after lowering.");
                break;
            case ErrorExpressionNode error:
                diagnostics.Report(
                    error.Location,
                    "Internal lowering error: parser error expression remains after post-semantic lowering.");
                break;
            case ListExpressionNode list:
                diagnostics.Report(
                    list.Location,
                    "Internal lowering error: compile-time list expression remains after lowering.");
                break;
            case TypeLiteralExpressionNode typeLiteral:
                diagnostics.Report(
                    typeLiteral.Location,
                    "Internal lowering error: compile-time type literal remains after lowering.");
                break;
            case FunctionExpressionNode function:
                diagnostics.Report(
                    function.Location,
                    "Internal lowering error: function expression remains after post-semantic lowering.");
                break;
            case ComputedMemberExpressionNode member:
                diagnostics.Report(
                    member.Location,
                    "Internal lowering error: computed member expression remains after lowering.");
                break;
            case GenericCallExpressionNode call:
                diagnostics.Report(
                    call.Location,
                    "Invalid Core CX: generic call remains after specialization.");
                break;
            case CallExpressionNode
            {
                Semantic.ResolvedCall.Function.TypeParameters.Count: > 0,
            } call:
                diagnostics.Report(
                    call.Location,
                    $"Invalid Core CX: call target '{call.Semantic.ResolvedCall!.Function.Name}' " +
                    "is still a generic function template.");
                break;
            case CallExpressionNode
            {
                Semantic.ResolvedCall: not null,
                Semantic.CoreDirectCall: null,
            } call:
                diagnostics.Report(
                    call.Location,
                    "Invalid Core CX: resolved call has no canonical direct-call facts.");
                break;
            case CallExpressionNode
            {
                Semantic.CoreDirectCall:
                {
                    IsInstance: true,
                    ReceiverAdaptation: null,
                },
                Callee: MemberExpressionNode,
            } call:
                diagnostics.Report(
                    call.Location,
                    "Invalid Core CX: instance call has no explicit receiver adaptation.");
                break;
            case CallExpressionNode
            {
                Callee: MemberExpressionNode
                {
                    Semantic.ResolvedCall.Function.TypeParameters.Count: > 0,
                } member,
                Semantic.ResolvedCall: null,
            } call:
                diagnostics.Report(
                    call.Location,
                    $"Invalid Core CX: member call target '{member.Semantic.ResolvedCall!.Function.Name}' " +
                    "is still a generic function template.");
                break;
            case BinaryExpressionNode
            {
                Semantic.ResolvedCall.Function.TypeParameters.Count: > 0,
            } binary:
                diagnostics.Report(
                    binary.Location,
                    $"Invalid Core CX: operator target '{binary.Semantic.ResolvedCall!.Function.Name}' " +
                    "is still a generic function template.");
                break;
            case BinaryExpressionNode
            {
                Semantic.ResolvedCall: not null,
                Semantic.CoreDirectCall: null,
            } binary:
                diagnostics.Report(
                    binary.Location,
                    "Invalid Core CX: resolved operator has no canonical direct-call facts.");
                break;
            case CallExpressionNode
            {
                Callee: MemberExpressionNode member,
                Semantic.CoreInterfaceCall: null,
            } call when IsInterfaceSlot(call, member):
                diagnostics.Report(
                    call.Location,
                    "Invalid Core CX: interface call has no resolved interface slot.");
                break;
            case CallExpressionNode
            {
                Callee: MemberExpressionNode member,
                Semantic.CoreDirectCall: null,
                Semantic.CoreExternCall: null,
                Semantic.ConstructorCall: null,
                Semantic.CoreInterfaceCall: null,
            } call when IsUnresolvedTypedMemberCall(member):
                diagnostics.Report(
                    call.Location,
                    $"Invalid Core CX: typed member call '{member.MemberName}' on " +
                    $"'{TypeRefFormatter.ToCxString(member.Target.Semantic.Type!)}' " +
                    "has no resolved call target.");
                break;
            case CallExpressionNode call when !HasCoreCallTarget(call):
                diagnostics.Report(
                    call.Location,
                    "Invalid Core CX: runtime call has no resolved function, "
                    + "extern, interface, constructor, or function-value target.");
                break;
        }
    }

    private static bool ConversionContainsNonCoreType(
        CoreValueConversionInfo conversion) =>
        conversion switch
        {
            CoreValueConversionInfo.Interface interfaceConversion =>
                ContainsNonCoreType(interfaceConversion.TargetType)
                || ContainsNonCoreType(interfaceConversion.SourceType),
            CoreValueConversionInfo.TaggedUnion taggedUnionConversion =>
                ContainsNonCoreType(taggedUnionConversion.TargetType),
            _ => true,
        };

    private bool IsStaticSourceReference(MemberExpressionNode member)
    {
        if (ExpressionNameFacts.GetQualifiedName(member.Target) is not
            { } targetName)
        {
            return false;
        }

        return _enumNames.Contains(targetName)
            || _moduleQualifiers.Contains(targetName);
    }

    private static bool HasCoreCallTarget(CallExpressionNode call) =>
        call.Semantic.CoreDirectCall is not null
        || call.Semantic.CoreExternCall is not null
        || call.Semantic.ConstructorCall is not null
        || call.Semantic.CoreInterfaceCall is not null
        || call.Semantic.CoreIndirectCall is not null;

    private static bool RequiresCoreMemberAccess(
        MemberExpressionNode member)
    {
        if (member.Semantic.MemberReference is
                CoreMemberReferenceInfo.EnumMember
                or CoreMemberReferenceInfo.ModuleSymbol
            || member.Semantic.CoreDirectCall is
                {
                    IsInstance: false,
                })
        {
            return false;
        }

        return CoreExpressionTypeFacts.TryGet(member.Target) is not null;
    }

    private bool IsUnresolvedTypedMemberCall(MemberExpressionNode member)
    {
        if (member.Semantic.CoreDirectCall is not null
            || member.Semantic.Type is { } memberType
                && TypeRefFacts.UnwrapAlias(memberType) is TypeRef.Function
            || member.Target.Semantic.Type is not { } targetType)
        {
            return false;
        }

        var normalizedTarget = TypeRefFacts.StripPointersAndAliases(targetType);
        return TypeRefFacts.GetBaseName(normalizedTarget) is not { } targetName
            || !_taggedUnionNames.Contains(targetName);
    }

    private bool IsInterfaceSlot(
        CallExpressionNode call,
        MemberExpressionNode member)
    {
        if (member.Target.Semantic.Type is not { } targetType)
        {
            return false;
        }

        var type = TypeRefFacts.UnwrapAlias(targetType);
        if (type is TypeRef.Pointer pointer)
        {
            type = TypeRefFacts.UnwrapAlias(pointer.Element);
        }

        return TypeRefFacts.GetBaseName(type) is { } name
            && _interfaces.TryGetValue(name, out var interfaceNode)
            && interfaceNode.Methods.Any(method =>
                string.Equals(
                    method.Name,
                    member.MemberName,
                    StringComparison.Ordinal)
                && method.Parameters.Count == call.Arguments.Count);
    }
}
