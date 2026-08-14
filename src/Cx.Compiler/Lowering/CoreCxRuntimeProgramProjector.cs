using Cx.Compiler.Syntax;
using Cx.Compiler.Syntax.Nodes;

namespace Cx.Compiler.Lowering;

/// <summary>
/// Projects validated Core CX into the declaration inventory consumed by
/// runtime backends. Generic templates and front-end-only declarations remain
/// available before this boundary, but never reach target lowering.
/// </summary>
internal static class CoreCxRuntimeProgramProjector
{
    public static ProgramNode Project(ProgramNode program)
    {
        if (!program.Semantic.IsCoreCxValidated)
        {
            throw new InvalidOperationException(
                "Core CX runtime projection requires a validated Core CX program.");
        }

        var declarations = program.Declarations
            .Select(ProjectDeclaration)
            .Where(declaration => declaration is not null)
            .Cast<TopLevelNode>()
            .ToList();
        var projected = SyntaxNode.CloneMetadata(
            program,
            program with { Declarations = declarations });
        projected.Semantic.IsCoreCxRuntimeProjected = true;
        return projected;
    }

    private static TopLevelNode? ProjectDeclaration(
        TopLevelNode declaration) =>
        declaration switch
        {
            IncludeNode or CDeclareNode or InterfaceNode => declaration,
            ExternFunctionNode function
                when !function.IsHeaderDeclaration
                    && function.TypeParameters.Count == 0 => function,
            TypeAliasNode typeAlias
                when !typeAlias.IsHeaderDeclaration => typeAlias,
            EnumNode enumNode
                when !enumNode.IsHeaderDeclaration => enumNode,
            StructNode structNode
                when !structNode.IsHeaderDeclaration
                    && structNode.TypeParameters.Count == 0 =>
                SyntaxNode.CloneMetadata(
                    structNode,
                    structNode with
                    {
                        GenericConstraints = [],
                        Requirements = [],
                        Members = structNode.Fields,
                    }),
            TypeAdapterNode adapter =>
                SyntaxNode.CloneMetadata(
                    adapter,
                    adapter with { Members = [] }),
            TaggedUnionNode taggedUnion
                when !taggedUnion.IsHeaderDeclaration =>
                SyntaxNode.CloneMetadata(
                    taggedUnion,
                    taggedUnion with { Methods = [] }),
            GlobalVariableNode global
                when !global.IsHeaderDeclaration => global,
            FunctionNode function
                when !function.IsCompileTime
                    && function.TypeParameters.Count == 0 => function,
            _ => null,
        };
}
