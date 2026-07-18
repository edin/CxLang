using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.CompileTime;

internal sealed record CompileTimeIntrinsicContext(
    Location Location,
    IReadOnlyList<CompileTimeValue> Arguments,
    ICompileTimeReflection Reflection,
    DiagnosticBag Diagnostics,
    Func<ExpressionNode, CompileTimeValue?> Evaluate);

internal interface ICompileTimeIntrinsic
{
    string Name { get; }

    CompileTimeValue? Invoke(CompileTimeIntrinsicContext context);
}

internal sealed class CompileTimeIntrinsicRegistry
{
    private readonly Dictionary<string, ICompileTimeIntrinsic> _intrinsics = new(StringComparer.Ordinal);

    public bool Register(ICompileTimeIntrinsic intrinsic) =>
        _intrinsics.TryAdd(intrinsic.Name, intrinsic);

    public bool TryGet(string name, out ICompileTimeIntrinsic intrinsic) =>
        _intrinsics.TryGetValue(name, out intrinsic!);

    public static CompileTimeIntrinsicRegistry CreateDefault()
    {
        var registry = new CompileTimeIntrinsicRegistry();
        registry.Register(new ConcatCompileTimeIntrinsic());
        registry.Register(new AsNameCompileTimeIntrinsic());
        registry.Register(new FieldsCompileTimeIntrinsic());
        registry.Register(new NameCompileTimeIntrinsic());
        registry.Register(new TypeCompileTimeIntrinsic());
        registry.Register(new AttributesCompileTimeIntrinsic());
        registry.Register(new HasAttributeCompileTimeIntrinsic());
        registry.Register(new ArgumentsCompileTimeIntrinsic());
        registry.Register(new ValueCompileTimeIntrinsic());
        registry.Register(new TypeKindCompileTimeIntrinsic());
        registry.Register(new IsTypeCompileTimeIntrinsic());
        registry.Register(new ElementTypeCompileTimeIntrinsic());
        registry.Register(new TypeArgumentsCompileTimeIntrinsic());
        registry.Register(new RequirementMatchCompileTimeIntrinsic());
        registry.Register(new SatisfiesCompileTimeIntrinsic());
        registry.Register(new DeclaresRequirementCompileTimeIntrinsic());
        registry.Register(new CompileErrorCompileTimeIntrinsic());
        return registry;
    }
}
