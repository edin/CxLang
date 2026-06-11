# Semantic Refactoring Notes

This folder is moving toward a semantic pipeline where syntax is parsed once, then later passes work from AST nodes, `TypeNode.Syntax`, and `TypeRef` instead of reparsing strings.

## Direction

- Prefer AST and `TypeRef` over string parsing.
- Keep `SemanticAnalyzer` as an orchestration pass, not a home for every semantic rule.
- Put focused checks into small analyzers under `Analyzers/`.
- Reuse shared services such as `TypeRefParser`, `TypeSyntaxTypeRefConverter`, `TypeCompatibility`, `TypeSystem`, `CallResolver`, and `SymbolSuggestionService`.
- When a pass needs type structure, use `TypeNode.ToTypeRef(...)`, `TypeNode.Semantic.Type`, or `TypeRefParser.Parse(...)`.
- Keep raw string fallbacks only for genuinely raw syntax, C escape hatches, or temporary compatibility paths.

## Avoid

- Adding new `TryParse...` helpers that scan type strings locally.
- Splitting generic arguments by hand in semantic passes.
- Checking expression operators by scanning `ExpressionNode.SourceText`.
- Adding regex-based type substitution or type discovery when a `TypeRef` rewrite can do the job.
- Expanding `SemanticAnalyzer` with new rule-specific methods.

## Current Shape

`SemanticAnalyzer` should coordinate:

- pass setup
- global/function/member traversal
- scope dictionaries passed to focused analyzers
- high-level statement dispatch

Focused analyzers currently include:

- `AttributeSemanticAnalyzer`
- `RequirementDeclarationAnalyzer`
- `TypeUsageAnalyzer`
- `AssignmentSemanticAnalyzer`
- `ReturnSemanticAnalyzer`
- `MatchSemanticAnalyzer`
- `ForeachSemanticAnalyzer`
- `ExpressionSemanticAnalyzer`
- `DefiniteAssignmentAnalyzer`
- `ReturnFlowAnalyzer`
- `ModuleVisibilityAnalyzer`

Shared semantic helpers currently include:

- `TypeRef`
- `TypeRefParser`
- `TypeRefFormatter`
- `TypeRefRewriter`
- `TypeSyntaxTypeRefConverter`
- `TypeCompatibility`
- `TypeSystem`
- `RequirementMatcher`
- `CallResolver`
- `MethodCallResolver`
- `SymbolSuggestionService`

## Refactoring Checklist

When touching a semantic pass:

1. Check if it is parsing type text with `IndexOf`, `LastIndexOf`, `Split`, `Regex`, or local `TryParse...`.
2. Replace type text parsing with `TypeRef` or `TypeSyntaxNode` traversal.
3. Preserve behavior with a focused compiler test before deleting the fallback.
4. Keep diagnostics identical unless improving the diagnostic is the point of the change.
5. Run:

```powershell
dotnet build src\Cx.Cli\Cx.Cli.csproj --no-restore
dotnet test Cx.sln --no-restore
dotnet run --project src\Cx.Cli -- test --std --no-build
```

## Known Hotspots

These still contain local string parsing or regex work that should gradually move to `TypeRef` or AST traversal:

- `Analyzers/ModuleVisibilityAnalyzer.cs`
  - `TryParseFunctionType`
  - `TryParseGenericUse`
  - `SplitGenericArguments`
  - `StripPointer`
  - type-name discovery should walk `TypeRef` / `TypeSyntaxNode`

- `Analyzers/TypeUsageAnalyzer.cs`
  - `TryParseFunctionType`
  - `TryParseGenericUse`
  - `FindMatching...`
  - `SplitGenericArguments`
  - type-name discovery should walk `TypeRef` / `TypeSyntaxNode`

- `ExpressionTypeResolver.cs`
  - numeric literal regexes are acceptable short term
  - generic/pointer helpers should move toward `TypeRef`

- `RequirementMatcher.cs`
  - generic owner/type matching still uses string helpers in places
  - requirement matching should increasingly compare `ResolvedType` / `ResolvedMethod`

- `ScopeResolver.cs`
  - generic receiver parsing still uses string helpers
  - method lookup should keep moving toward `TypeRef`

- `TypeInferencePass.cs`
  - type-parameter occurrence checks still use regex
  - generic inference should keep moving toward `TypeRef`

- `GenericTypeStringRewriter.cs`
  - keep only as a compatibility bridge
  - prefer `TypeRefRewriter`

## Recent Cleanup Wins

- `<=>` semantic checking now uses `BinaryExpressionNode` instead of `TrySplitTopLevelSpaceship`.
- Null arithmetic checking now walks expression AST for normal expressions.
- `CallResolver` no longer parses function pointer type strings with a local `TryParseFunctionType`.
- `TypeRef.Function` now carries `IsVariadic`, so function pointer compatibility can compare variadic function types correctly.

