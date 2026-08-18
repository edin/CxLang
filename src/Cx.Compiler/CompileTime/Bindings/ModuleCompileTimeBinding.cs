using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed class ModuleCompileTimeBinding : CompileTimeTypeBinding
{
    public override string ScriptTypeName => "Module";

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

    [CompileTimeProperty("globals")]
    private IReadOnlyList<GlobalVariableNode> Globals(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Globals;

    [CompileTimeProperty("public_globals")]
    private IEnumerable<GlobalVariableNode> PublicGlobals(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Globals.Where(IsPublic);

    [CompileTimeProperty("constants")]
    private IReadOnlyList<CompileTimeConstantNode> Constants(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Constants;

    [CompileTimeProperty("public_constants")]
    private IEnumerable<CompileTimeConstantNode> PublicConstants(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Constants.Where(IsPublic);

    [CompileTimeProperty("interfaces")]
    private IReadOnlyList<InterfaceNode> Interfaces(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Interfaces;

    [CompileTimeProperty("public_interfaces")]
    private IEnumerable<InterfaceNode> PublicInterfaces(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Interfaces.Where(IsPublic);

    [CompileTimeProperty("requirements")]
    private IReadOnlyList<RequirementNode> Requirements(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Requirements;

    [CompileTimeProperty("public_requirements")]
    private IEnumerable<RequirementNode> PublicRequirements(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Requirements.Where(IsPublic);

    [CompileTimeProperty("attribute_declarations")]
    private IReadOnlyList<AttributeDeclarationNode> AttributeDeclarations(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.AttributeDeclarations;

    [CompileTimeProperty("public_attribute_declarations")]
    private IEnumerable<AttributeDeclarationNode> PublicAttributeDeclarations(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.AttributeDeclarations.Where(IsPublic);

    [CompileTimeProperty("attributes")]
    private IEnumerable<Cx.Compiler.Syntax.SyntaxNode> Attributes(
        CompileTimePropertyContext context,
        CompileTimeValue.Module module) => module.Value.Attributes;

    [CompileTimeMethod("attribute")]
    private CompileTimeValue Attribute(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string attributeName) =>
        module.Value.Attributes.FirstOrDefault(attribute =>
            string.Equals(attribute.Name, attributeName, StringComparison.Ordinal)) is { } attribute
                ? new CompileTimeValue.Syntax(attribute)
                : new CompileTimeValue.Null();

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

    [CompileTimeMethod("global")]
    private CompileTimeMethodResult Global(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => FindDeclaration(module, module.Value.Globals, name, "global", context);

    [CompileTimeMethod("constant")]
    private CompileTimeMethodResult Constant(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => FindDeclaration(module, module.Value.Constants, name, "constant", context);

    [CompileTimeMethod("interface")]
    private CompileTimeMethodResult Interface(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => FindDeclaration(module, module.Value.Interfaces, name, "interface", context);

    [CompileTimeMethod("find_interface")]
    private CompileTimeMethodResult FindInterface(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => Interface(context, module, name);

    [CompileTimeMethod("requirement")]
    private CompileTimeMethodResult Requirement(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => FindDeclaration(module, module.Value.Requirements, name, "requirement", context);

    [CompileTimeMethod("attribute_declaration")]
    private CompileTimeMethodResult AttributeDeclaration(
        CompileTimeMethodContext context,
        CompileTimeValue.Module module,
        string name) => FindDeclaration(
            module,
            module.Value.AttributeDeclarations,
            name,
            "attribute declaration",
            context);

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

    private static CompileTimeMethodResult FindDeclaration<T>(
        CompileTimeValue.Module module,
        IReadOnlyList<T> declarations,
        string name,
        string kind,
        CompileTimeMethodContext context)
        where T : TopLevelNode
    {
        var declaration = declarations.FirstOrDefault(candidate =>
            string.Equals(GetName(candidate), name, StringComparison.Ordinal));
        if (declaration is null)
        {
            context.Diagnostics.Report(
                context.Location,
                $"Compile-time module '{module.Value.Name}' does not contain {kind} '{name}'.");
            return new CompileTimeMethodResult.Failed();
        }

        return CompileTimeMethodResult.From(new CompileTimeValue.Syntax(declaration));
    }

    private static string GetName(TopLevelNode declaration) => declaration switch
    {
        GlobalVariableNode global => global.Name,
        CompileTimeConstantNode constant => constant.Name,
        InterfaceNode interfaceNode => interfaceNode.Name,
        RequirementNode requirement => requirement.Name,
        AttributeDeclarationNode attribute => attribute.Name,
        _ => string.Empty,
    };
}
