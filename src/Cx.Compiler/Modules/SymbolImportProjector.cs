using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Modules;

internal static class SymbolImportProjector
{
    public static ProgramNode Project(
        ProgramNode program,
        IReadOnlyDictionary<string, string> symbols)
    {
        var typeNames = ImportedTypeRewriter.GetDeclaredTypeNames(program);
        return program with
        {
            CDeclarations = program.CDeclarations.Select(declaration => ProjectSymbolImportCDeclaration(declaration, symbols, typeNames)).ToList(),
            ExternFunctions = program.ExternFunctions
                .Where(function => symbols.ContainsKey(function.Name))
                .Select(function => RenameExternFunction(function, symbols[function.Name], symbols, typeNames))
                .ToList(),
            TypeAliases = program.TypeAliases
                .Where(typeAlias => symbols.ContainsKey(typeAlias.Name))
                .Select(typeAlias => typeAlias with
                {
                    Name = symbols[typeAlias.Name],
                    TargetTypeNode = ImportedTypeRewriter.Project(typeAlias.TargetTypeNode, symbols, typeNames),
                })
                .ToList(),
            Enums = program.Enums
                .Where(enumNode => symbols.ContainsKey(enumNode.Name) || enumNode.Members.Any(member => symbols.ContainsKey(member.Name)))
                .Select(enumNode => enumNode with
                {
                    Name = symbols.GetValueOrDefault(enumNode.Name, enumNode.Name),
                    DataFields = enumNode.DataFields?
                        .Select(field => field with
                        {
                            TypeNode = ImportedTypeRewriter.Project(field.TypeNode, symbols, typeNames),
                        })
                        .ToList(),
                    Members = enumNode.Members
                        .Where(member => symbols.ContainsKey(member.Name))
                        .Select(member => member with { Name = symbols[member.Name] })
                        .ToList(),
                })
                .ToList(),
            Interfaces = program.Interfaces
                .Where(interfaceNode => symbols.ContainsKey(interfaceNode.Name))
                .Select(interfaceNode => interfaceNode with { Name = symbols[interfaceNode.Name] })
                .ToList(),
            Structs = program.Structs
                .Where(structNode => symbols.ContainsKey(structNode.Name))
                .Select(structNode => structNode with
                {
                    Name = symbols[structNode.Name],
                    Fields = structNode.Fields.Select(field => field with { TypeNode = ImportedTypeRewriter.Project(field.TypeNode, symbols, typeNames) }).ToList(),
                })
                .ToList(),
            TypeAdapters = program.TypeAdapters
                .Where(adapter => symbols.ContainsKey(adapter.Name))
                .Select(adapter => adapter with
                {
                    Name = symbols[adapter.Name],
                    BaseTypeNode = ImportedTypeRewriter.Project(adapter.BaseTypeNode, symbols, typeNames),
                    Methods = adapter.Methods.Select(method => method with
                    {
                        OwnerTypeNode = ImportedTypeRewriter.Rename(method.OwnerTypeNode, symbols[adapter.Name]),
                        ReturnTypeNode = ImportedTypeRewriter.Project(method.ReturnTypeNode, symbols, typeNames),
                        Parameters = method.Parameters.Select(parameter => RenameParameter(parameter, symbols, typeNames)).ToList(),
                    }).ToList(),
                })
                .ToList(),
            Extensions = program.Extensions
                .Where(extension => symbols.ContainsKey(ImportedTypeRewriter.GetText(extension.TargetTypeNode)))
                .Select(extension => extension with
                {
                    TargetTypeNode = ImportedTypeRewriter.Rename(extension.TargetTypeNode, symbols[ImportedTypeRewriter.GetText(extension.TargetTypeNode)]),
                    Methods = extension.Methods.Select(method => method with
                    {
                        OwnerTypeNode = ImportedTypeRewriter.Rename(method.OwnerTypeNode, symbols[ImportedTypeRewriter.GetText(extension.TargetTypeNode)]),
                        ReturnTypeNode = ImportedTypeRewriter.Project(method.ReturnTypeNode, symbols, typeNames),
                        Parameters = method.Parameters.Select(parameter => RenameParameter(parameter, symbols, typeNames)).ToList(),
                    }).ToList(),
                })
                .ToList(),
            TaggedUnions = program.TaggedUnions
                .Where(union => symbols.ContainsKey(union.Name))
                .Select(union => union with
                {
                    Name = symbols[union.Name],
                    Variants = union.Variants.Select(variant => variant with { TypeNode = ImportedTypeRewriter.Project(variant.TypeNode, symbols, typeNames) }).ToList(),
                })
                .ToList(),
            GlobalVariables = program.GlobalVariables
                .Where(global => symbols.ContainsKey(global.Name))
                .Select(global => global with
                {
                    Name = symbols[global.Name],
                    TypeNode = ImportedTypeRewriter.Project(global.TypeNode, symbols, typeNames),
                })
                .ToList(),
            CompileTimeConstants = program.CompileTimeConstants
                .Where(constant => symbols.ContainsKey(constant.Name))
                .Select(constant => constant with
                {
                    Name = symbols[constant.Name],
                    TypeNode = ImportedTypeRewriter.Project(
                        constant.TypeNode,
                        symbols,
                        typeNames),
                })
                .ToList(),
            Functions = program.Functions
                .Where(function => function.OwnerTypeNode is not null || symbols.ContainsKey(function.Name))
                .Select(function => function.OwnerTypeNode is null
                    ? RenameFunction(function, symbols[function.Name], symbols, typeNames)
                    : function)
                .ToList(),
        };
    }

    private static CDeclareNode ProjectSymbolImportCDeclaration(
        CDeclareNode declaration,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlySet<string> typeNames) =>
        declaration with
        {
            TypeAliases = declaration.TypeAliases
                .Where(typeAlias => symbols.ContainsKey(typeAlias.Name))
                .Select(typeAlias => typeAlias with
                {
                    Name = symbols[typeAlias.Name],
                    TargetTypeNode = ImportedTypeRewriter.Project(typeAlias.TargetTypeNode, symbols, typeNames),
                })
                .ToList(),
            Enums = declaration.Enums
                .Where(enumNode => symbols.ContainsKey(enumNode.Name) || enumNode.Members.Any(member => symbols.ContainsKey(member.Name)))
                .Select(enumNode => enumNode with
                {
                    Name = symbols.GetValueOrDefault(enumNode.Name, enumNode.Name),
                    DataFields = enumNode.DataFields?
                        .Select(field => field with
                        {
                            TypeNode = ImportedTypeRewriter.Project(field.TypeNode, symbols, typeNames),
                        })
                        .ToList(),
                    Members = enumNode.Members
                        .Where(member => symbols.ContainsKey(member.Name))
                        .Select(member => member with { Name = symbols[member.Name] })
                        .ToList(),
                })
                .ToList(),
            Structs = declaration.Structs
                .Where(structNode => symbols.ContainsKey(structNode.Name))
                .Select(structNode => structNode with
                {
                    Name = symbols[structNode.Name],
                    Fields = structNode.Fields.Select(field => field with { TypeNode = ImportedTypeRewriter.Project(field.TypeNode, symbols, typeNames) }).ToList(),
                })
                .ToList(),
            Unions = declaration.Unions
                .Where(union => symbols.ContainsKey(union.Name))
                .Select(union => union with
                {
                    Name = symbols[union.Name],
                    Variants = union.Variants.Select(variant => variant with { TypeNode = ImportedTypeRewriter.Project(variant.TypeNode, symbols, typeNames) }).ToList(),
                })
                .ToList(),
            Constants = declaration.Constants
                .Where(global => symbols.ContainsKey(global.Name))
                .Select(global => global with
                {
                    Name = symbols[global.Name],
                    TypeNode = ImportedTypeRewriter.Project(global.TypeNode, symbols, typeNames),
                })
                .ToList(),
            Functions = declaration.Functions
                .Where(function => symbols.ContainsKey(function.Name))
                .Select(function => RenameExternFunction(function, symbols[function.Name], symbols, typeNames))
                .ToList(),
        };

    private static ExternFunctionNode RenameExternFunction(
        ExternFunctionNode function,
        string visibleName,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlySet<string> typeNames) =>
        function with
        {
            Name = visibleName,
            ReturnTypeNode = ImportedTypeRewriter.Project(function.ReturnTypeNode, symbols, typeNames),
            Parameters = function.Parameters.Select(parameter => RenameParameter(parameter, symbols, typeNames)).ToList(),
        };

    private static FunctionNode RenameFunction(
        FunctionNode function,
        string visibleName,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlySet<string> typeNames) =>
        function with
        {
            Name = visibleName,
            ReturnTypeNode = ImportedTypeRewriter.Project(function.ReturnTypeNode, symbols, typeNames),
            Parameters = function.Parameters.Select(parameter => RenameParameter(parameter, symbols, typeNames)).ToList(),
        };

    private static ParameterNode RenameParameter(
        ParameterNode parameter,
        IReadOnlyDictionary<string, string> symbols,
        IReadOnlySet<string> typeNames) =>
        parameter.IsVariadic
            ? parameter
            : parameter with { TypeNode = ImportedTypeRewriter.Project(parameter.TypeNode, symbols, typeNames) };
}
