using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ModuleCompileTimeBinding : CompileTimeTypeBinding
{
    public override Type ReceiverType => typeof(CompileTimeValue.Module);

    [CompileTimeProperty("name")]
    private string Name(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Name;

    [CompileTimeProperty("functions")]
    private IEnumerable<Cx.Compiler.Syntax.SyntaxNode> Functions(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) =>
        FunctionList(module.Value.Functions);

    [CompileTimeProperty("public_functions")]
    private IEnumerable<Cx.Compiler.Syntax.SyntaxNode> PublicFunctions(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) =>
        FunctionList(module.Value.Functions.Where(IsPublic));

    [CompileTimeProperty("types")]
    private IEnumerable<Cx.Compiler.Semantic.TypeRef> Types(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) =>
        TypeList(module.Value.Types);

    [CompileTimeProperty("public_types")]
    private IEnumerable<Cx.Compiler.Semantic.TypeRef> PublicTypes(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) =>
        TypeList(module.Value.Types.Where(type => type.Declaration.IsPublic));

    [CompileTimeMethod("type")]
    private CompileTimeMethodResult Type(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string typeName) =>
        FindType(module, typeName, context, publicOnly: false);

    [CompileTimeMethod("public_type")]
    private CompileTimeMethodResult PublicType(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string typeName) =>
        FindType(module, typeName, context, publicOnly: true);

    private static IEnumerable<Cx.Compiler.Syntax.SyntaxNode> FunctionList(
        IEnumerable<Cx.Compiler.Syntax.SyntaxNode> functions) => functions;

    private static IEnumerable<Cx.Compiler.Semantic.TypeRef> TypeList(
        IEnumerable<ReflectedModuleType> types) =>
        types.Select(type => type.Type);

    private static CompileTimeMethodResult FindType(
        CompileTimeValue.Module module,
        string typeName,
        CompileTimeMethodContext context,
        bool publicOnly)
    {
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
