namespace Cx.Compiler.C;

internal sealed record CTranslationUnit(IReadOnlyList<CTranslationUnitItem> Items);

internal abstract record CTranslationUnitItem;

internal sealed record CComment(string Text) : CTranslationUnitItem;

internal sealed record CBlankLine : CTranslationUnitItem;

internal sealed record CInclude(string Path, bool IsSystem) : CTranslationUnitItem;

internal sealed record CEnumDeclaration(
    string Name,
    IReadOnlyList<CEnumMember> Members) : CTranslationUnitItem;

internal sealed record CEnumMember(string Name, string? Value);

internal sealed record CDataEnumDeclaration(
    CEnumDeclaration Enum,
    string CountName,
    string DataTypeName,
    string TableName,
    IReadOnlyList<CFieldDeclaration> Fields,
    IReadOnlyList<CDataEnumRow> Rows) : CTranslationUnitItem;

internal sealed record CDataEnumRow(
    string EnumMemberName,
    IReadOnlyList<CInitializerField> Values);

internal abstract record CTypeRef;

internal sealed record CNamedTypeRef(string Name) : CTypeRef;

internal sealed record CStructTypeRef(string Name) : CTypeRef;

internal sealed record CPointerTypeRef(CTypeRef Element) : CTypeRef;

internal sealed record CConstTypeRef(CTypeRef Element) : CTypeRef;

internal sealed record CFixedArrayTypeRef(CTypeRef Element, string Length) : CTypeRef;

internal sealed record CFunctionTypeRef(
    CTypeRef ReturnType,
    IReadOnlyList<CParameterDeclaration> Parameters) : CTypeRef;

internal sealed record CFieldDeclaration(CTypeRef Type, string Name);

internal sealed record CParameterDeclaration(CTypeRef Type, string Name, bool IsVariadic = false);

internal sealed record CVariableDeclaration(CTypeRef Type, string Name, bool IsConst = false);

internal sealed record CFunctionSignature(
    CTypeRef ReturnType,
    string Name,
    IReadOnlyList<CParameterDeclaration> Parameters);

internal sealed record CStructDeclaration(
    string Name,
    IReadOnlyList<CFieldDeclaration> Fields) : CTranslationUnitItem;

internal sealed record CTaggedUnionDeclaration(
    string Name,
    bool IsRaw,
    IReadOnlyList<CTaggedUnionVariantDeclaration> Variants) : CTranslationUnitItem;

internal sealed record CTaggedUnionVariantDeclaration(
    string Name,
    CTypeRef Type,
    CFieldDeclaration FieldDeclaration);

internal sealed record CTypeAliasDeclaration(
    string Name,
    CTypeRef TargetType) : CTranslationUnitItem;

internal sealed record CFunctionDeclaration(
    CFunctionSignature Signature) : CTranslationUnitItem;

internal sealed record CFunctionDefinition(
    CFunctionSignature Signature,
    IReadOnlyList<CStatementNode> Body) : CTranslationUnitItem;

internal sealed record CGlobalDeclaration(
    CVariableDeclaration Declaration,
    CExpression? Initializer) : CTranslationUnitItem;

internal sealed record CExternGlobalDeclaration(
    CVariableDeclaration Declaration) : CTranslationUnitItem;

internal abstract record CStatementNode;

internal sealed record CBlockStatement(IReadOnlyList<CStatementNode> Body) : CStatementNode;

internal sealed record CLocalDeclarationStatement(
    CVariableDeclaration Declaration,
    CExpression? Initializer) : CStatementNode;

internal sealed record CReturnStatement(CExpression? Expression) : CStatementNode;

internal sealed record CBreakStatement : CStatementNode;

internal sealed record CContinueStatement : CStatementNode;

internal sealed record CExpressionStatement(CExpression Expression) : CStatementNode;

internal sealed record CIfStatement(
    CExpression Condition,
    IReadOnlyList<CStatementNode> ThenBody,
    CElseClause? ElseClause) : CStatementNode;

internal sealed record CWhileStatement(
    CExpression Condition,
    IReadOnlyList<CStatementNode> Body) : CStatementNode;

internal sealed record CForStatement(
    CForInitializerNode Initializer,
    CExpression Condition,
    CExpression Increment,
    IReadOnlyList<CStatementNode> Body) : CStatementNode;

internal abstract record CForInitializerNode;

internal sealed record CEmptyForInitializer : CForInitializerNode;

internal sealed record CDeclarationForInitializer(
    CVariableDeclaration Declaration,
    CExpression? Initializer) : CForInitializerNode;

internal sealed record CExpressionForInitializer(
    CExpression Expression) : CForInitializerNode;

internal sealed record CSwitchStatement(
    CExpression Expression,
    IReadOnlyList<CSwitchCase> Cases,
    IReadOnlyList<CStatementNode> DefaultBody) : CStatementNode;

internal sealed record CSwitchCase(
    CExpression Pattern,
    IReadOnlyList<CStatementNode> Body);

internal abstract record CElseClause;

internal sealed record CElseIfClause(CIfStatement IfStatement) : CElseClause;

internal sealed record CElseBlockClause(IReadOnlyList<CStatementNode> Body) : CElseClause;
