using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CEnumDeclaration ToCTypeIdEnum(IReadOnlyList<InterfaceImplementation> implementations) =>
        new(
            "CxTypeId",
            new[] { new CEnumMember("CX_TYPE_UNKNOWN", "0") }
                .Concat(implementations
                .Select(implementation => implementation.Struct.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(name => new CEnumMember(GetTypeIdName(name), null)))
                .ToList());

    private static CStructDeclaration ToCInterfaceVTableStruct(InterfaceNode interfaceNode)
    {
        var fields = new List<CFieldDeclaration> { new(new CNamedTypeRef("CxTypeId"), "type_id") };
        foreach (var method in interfaceNode.Methods)
        {
            var parameters = new List<CParameterDeclaration>
            {
                new(new CPointerTypeRef(new CNamedTypeRef("void")), "state"),
            };
            parameters.AddRange(method.Parameters.Select(parameter => LowerParameter(parameter)));
            fields.Add(new CFieldDeclaration(
                new CFunctionTypeRef(
                    LowerReturnType(method.ReturnTypeNode, InterfaceMethodReturnTypeText(method)),
                    parameters),
                method.Name));
        }

        return new CStructDeclaration(GetInterfaceVTableName(interfaceNode.Name), fields);
    }

    private static CStructDeclaration ToCInterfaceValueStruct(InterfaceNode interfaceNode)
    {
        var fields = new List<CFieldDeclaration>
        {
            new(new CPointerTypeRef(new CNamedTypeRef("void")), "state"),
            new(
                new CPointerTypeRef(new CConstTypeRef(new CNamedTypeRef(GetInterfaceVTableName(interfaceNode.Name)))),
                "vtable"),
        };
        return new CStructDeclaration(interfaceNode.Name, fields);
    }

    private static CGlobalDeclaration ToCInterfaceVTableInstance(
        InterfaceImplementation implementation,
        IReadOnlyList<FunctionNode> functions)
    {
        var fields = new List<CInitializerField>
        {
            new("type_id", new CNameExpression(GetTypeIdName(implementation.Struct.Name))),
        };

        foreach (var method in implementation.Interface.Methods)
        {
            var concrete = functions.FirstOrDefault(function =>
                GetConcreteFunctionOwnerName(function) == implementation.Struct.Name
                && !function.IsStatic
                && function.Name == method.Name);
            if (concrete is null)
            {
                continue;
            }

            fields.Add(new CInitializerField(
                method.Name,
                new CCastExpression(
                    BuildInterfaceMethodSlotType(method),
                    new CNameExpression(GetCFunctionName(concrete)))));
        }

        return new CGlobalDeclaration(
            new CVariableDeclaration(
                new CNamedTypeRef(GetInterfaceVTableName(implementation.Interface.Name)),
                GetInterfaceVTableInstanceName(implementation.Struct.Name, implementation.Interface.Name),
                IsConst: true),
            new CInitializerExpression(Type: null, fields, Values: []));
    }

    private static CFunctionTypeRef BuildInterfaceMethodSlotType(InterfaceMethodNode method)
    {
        var parameters = new List<CParameterDeclaration>
        {
            new(new CPointerTypeRef(new CNamedTypeRef("void")), string.Empty),
        };
        parameters.AddRange(method.Parameters.Select(parameter => LowerParameter(parameter)));
        return new CFunctionTypeRef(
            LowerReturnType(method.ReturnTypeNode, InterfaceMethodReturnTypeText(method)),
            parameters);
    }

    private static CExternGlobalDeclaration ToCInterfaceVTableDeclaration(InterfaceImplementation implementation) =>
        new(new CVariableDeclaration(
            new CNamedTypeRef(GetInterfaceVTableName(implementation.Interface.Name)),
            GetInterfaceVTableInstanceName(implementation.Struct.Name, implementation.Interface.Name),
            IsConst: true));

    private static IReadOnlyList<InterfaceImplementation> GetInterfaceImplementations(
        ProgramNode program,
        IReadOnlyList<StructNode> structs)
    {
        var interfaces = program.Interfaces.ToDictionary(interfaceNode => interfaceNode.Name, StringComparer.Ordinal);
        return structs
            .Where(structNode => !structNode.IsHeaderDeclaration)
            .SelectMany(structNode => structNode.Requirements
                .Select(requirement => interfaces.TryGetValue(requirement.Name, out var interfaceNode)
                    ? new InterfaceImplementation(structNode, interfaceNode)
                    : null)
                .Where(implementation => implementation is not null)
                .Cast<InterfaceImplementation>())
            .GroupBy(implementation => (implementation.Struct.Name, implementation.Interface.Name))
            .Select(group => group.First())
            .ToList();
    }

    private static string GetInterfaceVTableName(string interfaceName) =>
        s_abiNames.InterfaceVTableName(interfaceName);

    private static string GetInterfaceVTableInstanceName(string structName, string interfaceName) =>
        s_abiNames.InterfaceVTableInstanceName(structName, interfaceName);

    private static string GetTypeIdName(string typeName) =>
        s_abiNames.TypeIdName(typeName);

    private sealed record InterfaceImplementation(StructNode Struct, InterfaceNode Interface);
}
