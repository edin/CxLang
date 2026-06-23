using Cx.Compiler.C;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler;

public sealed partial class CEmitter
{
    private static CEnumDeclaration ToCTypeIdEnum(
        CBackendContext backend,
        IReadOnlyList<InterfaceImplementation> implementations) =>
        new(
            "CxTypeId",
            new[] { new CEnumMember("CX_TYPE_UNKNOWN", "0") }
                .Concat(implementations
                .Select(implementation => implementation.Struct.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(name => new CEnumMember(GetTypeIdName(backend, name), null)))
                .ToList());

    private static CStructDeclaration ToCInterfaceVTableStruct(CBackendContext backend, InterfaceNode interfaceNode)
    {
        var fields = new List<CFieldDeclaration> { new(new CNamedTypeRef("CxTypeId"), "type_id") };
        foreach (var method in interfaceNode.Methods)
        {
            var parameters = new List<CParameterDeclaration>
            {
                new(new CPointerTypeRef(new CNamedTypeRef("void")), "state"),
            };
            parameters.AddRange(method.Parameters.Select(parameter => LowerParameter(backend, parameter)));
            fields.Add(new CFieldDeclaration(
                new CFunctionTypeRef(
                    LowerReturnType(backend, method.ReturnTypeNode, InterfaceMethodReturnTypeText(method)),
                    parameters),
                method.Name));
        }

        return new CStructDeclaration(GetInterfaceVTableName(backend, interfaceNode.Name), fields);
    }

    private static CStructDeclaration ToCInterfaceValueStruct(CBackendContext backend, InterfaceNode interfaceNode)
    {
        var fields = new List<CFieldDeclaration>
        {
            new(new CPointerTypeRef(new CNamedTypeRef("void")), "state"),
            new(
                new CPointerTypeRef(new CConstTypeRef(new CNamedTypeRef(GetInterfaceVTableName(backend, interfaceNode.Name)))),
                "vtable"),
        };
        return new CStructDeclaration(interfaceNode.Name, fields);
    }

    private static CGlobalDeclaration ToCInterfaceVTableInstance(
        CBackendContext backend,
        InterfaceImplementation implementation,
        IReadOnlyList<FunctionNode> functions)
    {
        var fields = new List<CInitializerField>
        {
            new("type_id", new CNameExpression(GetTypeIdName(backend, implementation.Struct.Name))),
        };

        foreach (var method in implementation.Interface.Methods)
        {
            var concrete = functions.FirstOrDefault(function =>
                GetConcreteFunctionOwnerName(backend, function) == implementation.Struct.Name
                && !function.IsStatic
                && function.Name == method.Name);
            if (concrete is null)
            {
                continue;
            }

            fields.Add(new CInitializerField(
                method.Name,
                new CCastExpression(
                    BuildInterfaceMethodSlotType(backend, method),
                    new CNameExpression(GetCFunctionName(backend, concrete)))));
        }

        return new CGlobalDeclaration(
            new CVariableDeclaration(
                new CNamedTypeRef(GetInterfaceVTableName(backend, implementation.Interface.Name)),
                GetInterfaceVTableInstanceName(backend, implementation.Struct.Name, implementation.Interface.Name),
                IsConst: true),
            new CInitializerExpression(Type: null, fields, Values: []));
    }

    private static CFunctionTypeRef BuildInterfaceMethodSlotType(CBackendContext backend, InterfaceMethodNode method)
    {
        var parameters = new List<CParameterDeclaration>
        {
            new(new CPointerTypeRef(new CNamedTypeRef("void")), string.Empty),
        };
        parameters.AddRange(method.Parameters.Select(parameter => LowerParameter(backend, parameter)));
        return new CFunctionTypeRef(
            LowerReturnType(backend, method.ReturnTypeNode, InterfaceMethodReturnTypeText(method)),
            parameters);
    }

    private static CExternGlobalDeclaration ToCInterfaceVTableDeclaration(
        CBackendContext backend,
        InterfaceImplementation implementation) =>
        new(new CVariableDeclaration(
            new CNamedTypeRef(GetInterfaceVTableName(backend, implementation.Interface.Name)),
            GetInterfaceVTableInstanceName(backend, implementation.Struct.Name, implementation.Interface.Name),
            IsConst: true));

    private static string GetInterfaceVTableName(CBackendContext backend, string interfaceName) =>
        backend.AbiNames.InterfaceVTableName(interfaceName);

    private static string GetInterfaceVTableInstanceName(CBackendContext backend, string structName, string interfaceName) =>
        backend.AbiNames.InterfaceVTableInstanceName(structName, interfaceName);

    private static string GetTypeIdName(CBackendContext backend, string typeName) =>
        backend.AbiNames.TypeIdName(typeName);
}
