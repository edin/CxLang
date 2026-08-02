using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class ImportedDeclarationQualifier
{
    public static ProgramNode Qualify(ProgramNode program, string alias)
    {
        var typeNames = ImportedTypeRewriter.GetDeclaredTypeNames(program);
        return program with
        {
            CDeclarations = program.CDeclarations.Select(declaration => QualifyCDeclaration(declaration, alias, typeNames)).ToList(),
            ExternFunctions = program.ExternFunctions.Select(function => QualifyExternFunction(function, alias, typeNames)).ToList(),
            TypeAliases = program.TypeAliases.Select(typeAlias => typeAlias with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, typeAlias.Name),
                TargetTypeNode = ImportedTypeRewriter.Qualify(typeAlias.TargetTypeNode, alias, typeNames),
            }).ToList(),
            Enums = program.Enums.Select(enumNode => enumNode with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, enumNode.Name),
                Members = enumNode.Members.Select(member => member with { Name = ImportedTypeRewriter.QualifyName(alias, member.Name) }).ToList(),
                DataFields = enumNode.DataFields?
                    .Select(field => field with { TypeNode = ImportedTypeRewriter.Qualify(field.TypeNode, alias, typeNames) })
                    .ToList(),
            }).ToList(),
            Interfaces = program.Interfaces.Select(interfaceNode => interfaceNode with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, interfaceNode.Name),
                Methods = interfaceNode.Methods.Select(method => method with
                {
                    ReturnTypeNode = ImportedTypeRewriter.Qualify(method.ReturnTypeNode, alias, typeNames),
                    Parameters = method.Parameters.Select(parameter => QualifyParameter(parameter, alias, typeNames)).ToList(),
                }).ToList(),
            }).ToList(),
            Structs = program.Structs.Select(structNode => (structNode with
                {
                    Name = ImportedTypeRewriter.QualifyName(alias, structNode.Name),
                }).WithFields(structNode.Fields
                    .Select(field => field with
                    {
                        TypeNode = ImportedTypeRewriter.Qualify(
                            field.TypeNode,
                            alias,
                            typeNames),
                    })
                    .ToList())).ToList(),
            TypeAdapters = program.TypeAdapters.Select(adapter => (adapter with
                {
                    Name = ImportedTypeRewriter.QualifyName(alias, adapter.Name),
                    BaseTypeNode = ImportedTypeRewriter.Qualify(
                        adapter.BaseTypeNode,
                        alias,
                        typeNames),
                }).WithMethods(adapter.Methods.Select(method => method with
                {
                    OwnerTypeNode = ImportedTypeRewriter.Qualify(
                        method.OwnerTypeNode ?? TypeNode.Named(method.Location, adapter.Name),
                        alias,
                        typeNames),
                    ReturnTypeNode = ImportedTypeRewriter.Qualify(method.ReturnTypeNode, alias, typeNames),
                    Parameters = method.Parameters.Select(parameter => QualifyParameter(parameter, alias, typeNames)).ToList(),
                }).ToList())).ToList(),
            Extensions = program.Extensions.Select(extension => (extension with
                {
                    TargetTypeNode = ImportedTypeRewriter.Qualify(
                        extension.TargetTypeNode,
                        alias,
                        typeNames),
                }).WithMethods(extension.Methods.Select(method => method with
                {
                    OwnerTypeNode = ImportedTypeRewriter.Qualify(
                        method.OwnerTypeNode ?? extension.TargetTypeNode,
                        alias,
                        typeNames),
                    ReturnTypeNode = ImportedTypeRewriter.Qualify(method.ReturnTypeNode, alias, typeNames),
                    Parameters = method.Parameters.Select(parameter => QualifyParameter(parameter, alias, typeNames)).ToList(),
                }).ToList())).ToList(),
            TaggedUnions = program.TaggedUnions.Select(union => union with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, union.Name),
                Variants = union.Variants.Select(variant => variant with { TypeNode = ImportedTypeRewriter.Qualify(variant.TypeNode, alias, typeNames) }).ToList(),
            }).ToList(),
            GlobalVariables = program.GlobalVariables.Select(global => global with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, global.Name),
                TypeNode = ImportedTypeRewriter.Qualify(global.TypeNode, alias, typeNames),
            }).ToList(),
            CompileTimeConstants = program.CompileTimeConstants.Select(constant => constant with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, constant.Name),
                TypeNode = ImportedTypeRewriter.Qualify(constant.TypeNode, alias, typeNames),
            }).ToList(),
            Functions = program.Functions.Select(function => function.OwnerTypeNode is null
                ? QualifyFunction(function, alias, typeNames) with { Name = ImportedTypeRewriter.QualifyName(alias, function.Name) }
                : function).ToList(),
        };
    }

    private static CDeclareNode QualifyCDeclaration(
        CDeclareNode declaration,
        string alias,
        IReadOnlySet<string> typeNames) =>
        declaration with
        {
            TypeAliases = declaration.TypeAliases.Select(typeAlias => typeAlias with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, typeAlias.Name),
                TargetTypeNode = ImportedTypeRewriter.Qualify(typeAlias.TargetTypeNode, alias, typeNames),
            }).ToList(),
            Enums = declaration.Enums.Select(enumNode => enumNode with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, enumNode.Name),
                Members = enumNode.Members.Select(member => member with { Name = ImportedTypeRewriter.QualifyName(alias, member.Name) }).ToList(),
                DataFields = enumNode.DataFields?
                    .Select(field => field with { TypeNode = ImportedTypeRewriter.Qualify(field.TypeNode, alias, typeNames) })
                    .ToList(),
            }).ToList(),
            Structs = declaration.Structs.Select(structNode => (structNode with
                {
                    Name = ImportedTypeRewriter.QualifyName(alias, structNode.Name),
                }).WithFields(structNode.Fields
                    .Select(field => field with
                    {
                        TypeNode = ImportedTypeRewriter.Qualify(
                            field.TypeNode,
                            alias,
                            typeNames),
                    })
                    .ToList())).ToList(),
            Unions = declaration.Unions.Select(union => union with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, union.Name),
                Variants = union.Variants.Select(variant => variant with { TypeNode = ImportedTypeRewriter.Qualify(variant.TypeNode, alias, typeNames) }).ToList(),
            }).ToList(),
            Constants = declaration.Constants.Select(global => global with
            {
                Name = ImportedTypeRewriter.QualifyName(alias, global.Name),
                TypeNode = ImportedTypeRewriter.Qualify(global.TypeNode, alias, typeNames),
            }).ToList(),
            Functions = declaration.Functions.Select(function => QualifyExternFunction(function, alias, typeNames)).ToList(),
        };

    private static ExternFunctionNode QualifyExternFunction(
        ExternFunctionNode function,
        string alias,
        IReadOnlySet<string> typeNames) =>
        function with
        {
            Name = ImportedTypeRewriter.QualifyName(alias, function.Name),
            ReturnTypeNode = ImportedTypeRewriter.Qualify(function.ReturnTypeNode, alias, typeNames),
            Parameters = function.Parameters.Select(parameter => QualifyParameter(parameter, alias, typeNames)).ToList(),
        };

    private static FunctionNode QualifyFunction(
        FunctionNode function,
        string alias,
        IReadOnlySet<string> typeNames) =>
        function with
        {
            ReturnTypeNode = ImportedTypeRewriter.Qualify(function.ReturnTypeNode, alias, typeNames),
            Parameters = function.Parameters.Select(parameter => QualifyParameter(parameter, alias, typeNames)).ToList(),
        };

    private static ParameterNode QualifyParameter(
        ParameterNode parameter,
        string alias,
        IReadOnlySet<string> typeNames) =>
        parameter.IsVariadic
            ? parameter
            : parameter with { TypeNode = ImportedTypeRewriter.Qualify(parameter.TypeNode, alias, typeNames) };
}
