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

    private static CompileTimePropertyResult FunctionList(IEnumerable<Cx.Compiler.Syntax.SyntaxNode> functions) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            functions.Select(function => new CompileTimeValue.Syntax(function)).ToList()));

    private static CompileTimePropertyResult TypeList(IEnumerable<ReflectedModuleType> types) =>
        CompileTimePropertyResult.From(new CompileTimeValue.List(
            types.Select(type => new CompileTimeValue.Type(type.Type)).ToList()));

    private static bool IsPublic(Cx.Compiler.Syntax.SyntaxNode function) =>
        function is TopLevelNode { IsPublic: true };
}
