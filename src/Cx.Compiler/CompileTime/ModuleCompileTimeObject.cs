using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ModuleCompileTimeObject : CompileTimeScriptObject
{
    public override Type ReceiverType => typeof(CompileTimeValue.Module);

    [CompileTimeProperty("name")]
    private CompileTimePropertyResult Name(
        CompileTimeValue.Module module,
        CompileTimePropertyContext context) =>
        CompileTimePropertyResult.From(new CompileTimeValue.String(module.Value.Name));

    [CompileTimeProperty("functions")]
    private CompileTimePropertyResult Functions(
        CompileTimeValue.Module module,
        CompileTimePropertyContext context) =>
        FunctionList(module.Value.Functions);

    [CompileTimeProperty("public_functions")]
    private CompileTimePropertyResult PublicFunctions(
        CompileTimeValue.Module module,
        CompileTimePropertyContext context) =>
        FunctionList(module.Value.Functions.Where(IsPublic));

    [CompileTimeProperty("types")]
    private CompileTimePropertyResult Types(
        CompileTimeValue.Module module,
        CompileTimePropertyContext context) =>
        TypeList(module.Value.Types);

    [CompileTimeProperty("public_types")]
    private CompileTimePropertyResult PublicTypes(
        CompileTimeValue.Module module,
        CompileTimePropertyContext context) =>
        TypeList(module.Value.Types.Where(type => type.Declaration.IsPublic));

    [CompileTimeMethod("type")]
    private CompileTimeMethodResult Type(
        CompileTimeValue.Module module,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context) =>
        FindType(module, arguments, context, publicOnly: false);

    [CompileTimeMethod("public_type")]
    private CompileTimeMethodResult PublicType(
        CompileTimeValue.Module module,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context) =>
        FindType(module, arguments, context, publicOnly: true);

    private static CompileTimePropertyResult FunctionList(IEnumerable<Cx.Compiler.Syntax.SyntaxNode> functions) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            functions.Select(function => new CompileTimeValue.Syntax(function)).ToList()));

    private static CompileTimePropertyResult TypeList(IEnumerable<ReflectedModuleType> types) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            types.Select(type => new CompileTimeValue.Type(type.Type)).ToList()));

    private static CompileTimeMethodResult FindType(
        CompileTimeValue.Module module,
        IReadOnlyList<CompileTimeValue> arguments,
        CompileTimeMethodContext context,
        bool publicOnly)
    {
        if (arguments is not [var nameValue]
            || CompileTimeConstructorFacts.GetName(nameValue) is not { } typeName)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time module method '{(publicOnly ? "public_type" : "type")}' expects exactly one type-name string or name argument.");
            return new CompileTimeMethodResult.Failed();
        }

        var reflectedType = module.Value.Types.FirstOrDefault(candidate =>
            string.Equals(CompileTimeTypeFacts.Name(candidate.Type), typeName, StringComparison.Ordinal));
        if (reflectedType is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time module '{module.Value.Name}' does not contain type '{typeName}'.");
            return new CompileTimeMethodResult.Failed();
        }

        if (publicOnly && !reflectedType.Declaration.IsPublic)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time type '{module.Value.Name}.{typeName}' is not public.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Type(reflectedType.Type));
    }

    private static bool IsPublic(Cx.Compiler.Syntax.SyntaxNode function) =>
        function is TopLevelNode { IsPublic: true };
}
