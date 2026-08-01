# CX Repository Guide for Coding Agents

## Project

CX is an experimental C-like language implemented in C# and targeting readable
C. The language, compiler internals, standard library, and tooling are still
evolving. Prefer small, coherent changes that preserve the existing typed AST
pipeline over compatibility hacks or text-based transformations.

The repository targets .NET 9 and is developed primarily on Windows with
PowerShell and GCC available.

## Repository Map

- `src/Cx.Compiler/` — lexer, parser, AST, compile-time system, semantic passes,
  lowering, C backend, completion, and embedded standard library.
- `src/Cx.Cli/` — `cx` command-line entry point and project orchestration.
- `tests/Cx.Compiler.Tests/` — compiler unit and integration tests.
- `src/Cx.Compiler/Std/` — embedded CX standard-library sources.
- `editors/vscode/` — VS Code extension and bundled language server.
- `examples/` — CX programs used for manual and configured-project testing.
- `scripts/verify.ps1` — complete repository verification gate.
- `site/` — Astro website for `cxlang.dev`.

Do not edit generated content under `bin/`, `obj/`, `node_modules/`, or the
published VS Code server unless the task specifically requires rebuilding it.

## Compiler Pipeline

`ProgramCompilationPipeline` is the best high-level entry point. The important
order is:

1. Load and parse core and user source programs.
2. Validate compile-time placeholder placement.
3. Build test programs when requested.
4. Analyze module visibility.
5. Project modules into the root program.
6. Expand macros and compile-time directives.
7. Apply pre-semantic lowering.
8. Optionally prune unreachable CX functions.
9. Resolve scopes, types, inference, and semantic call information.
10. Lower nested `try` fallback chains and resolve semantics again when needed.
11. Run semantic analyzers.
12. Apply post-semantic lowering.
13. Lower to the C AST, prune unused C declarations, and emit C.

Do not move a transformation between phases without checking which semantic
information it consumes and whether its output requires semantic resolution
again.

## AST Conventions

- `ProgramNode.Declarations` is the canonical top-level ownership list.
  Properties such as `Functions`, `Structs`, and `Enums` are typed projections;
  do not maintain parallel declaration collections.
- `CDeclareNode.Members` follows the same canonical-list rule.
- AST nodes carry source `Span`/`Location`. Preserve metadata when cloning or
  replacing nodes. Generated-origin metadata can be added later without storing
  raw source text in semantic nodes.
- Prefer structured `TypeNode`, `TypeSyntaxNode`, and `TypeRef` operations.
  Do not parse, compare, qualify, or rewrite types through ad-hoc strings.
  Reuse `TypeRefFacts`, `TypeRefRewriter`, `TypeRefFormatter`, converters, and
  the known-type factory methods.
- Prefer dedicated AST nodes for language constructs. Do not encode semantic
  state through source fragments, flags on unrelated nodes, or reparsing
  `ToSourceText()`.
- `AstChildren` is the canonical structural child map. Every new concrete
  `SyntaxNode` must be registered there; the completeness test will fail
  otherwise.
- Use `AstTraversal` for complete structural read-only traversal and
  `AstRewriter` for structural transformations.
- Use `ExecutableAstTraversal` when the query must inspect executable program
  expressions while excluding macro templates, metadata, and unexpanded
  compile-time blocks.
- Use `FunctionLocalBindingFacts` for structural discovery of `let`, `using`,
  `for`, `foreach`, and match-arm bindings. Scope-sensitive analysis still
  belongs in its dedicated analyzer.
- Macro declarations remain in the program AST after expansion. A generic walk
  over `ProgramNode` can therefore see reusable macro template bodies; do not
  treat those bodies as emitted runtime code.

## Semantic and Function Conventions

- `FunctionCatalog` is the canonical semantic inventory for free functions,
  methods, extensions, constrained extensions, adapters, and overload sets.
- Multiple functions with the same source name are expected. Select candidates
  through catalog queries and the shared overload/call-resolution services, not
  `FirstOrDefault()` by name.
- Preserve canonical function identity and resolved call information when
  specializing generics or retargeting calls.
- Primitive types and intrinsic operators are compiler-known semantic facts.
  Do not require fake source declarations for intrinsic behavior and do not
  allow an extension to redefine an identical intrinsic operator signature.
- Module visibility is semantic. Public API checks and cross-module access must
  use module/symbol facts rather than mangled-name string inspection.
- Diagnostics should use the most specific source span available and explain
  the semantic problem, not an internal lowering failure.

## Compile-Time System

- Compile-time values and script objects are typed internal values, separate
  from their behavior/dispatch objects.
- Intrinsic methods and properties should use the centralized registry and
  typed dispatch. Avoid growing name-based `if`/`switch` dispatch in the
  evaluator.
- Attributes, reflection objects, lists, types, parameters, and diagnostic
  values should remain representable through the common compile-time value
  model.
- Compile-time-only nodes must be expanded or diagnosed before C lowering.
  Add guards when a lowering phase assumes no compile-time residue.

## Lowering and C Backend

- Lower AST to AST. Avoid emitting or reparsing C/CX source strings as an
  intermediate representation.
- Keep pre-semantic and post-semantic lowering responsibilities distinct.
- Resource cleanup, `using`, reassignment cleanup, and return transfer are
  control-flow-sensitive. Do not replace their scoped logic with a flat walker.
- C reachability starts from configured entry points and retains required
  dependencies. References stored in metadata such as data-enum function values
  also count as uses.
- Generated C names must remain deterministic and module-safe.

## Working Practices

- Preserve unrelated working-tree changes. This repository is frequently
  refactored in a sequence of uncommitted slices.
- Prefer a focused refactor with behavior-preserving tests over broad mechanical
  rewrites.
- Before adding an abstraction, search for the existing catalog, facts,
  resolver, traversal, or pipeline service that owns the concept.
- When adding syntax or an AST node, update lexer/parser handling, spans,
  `AstChildren`, rewriters, semantic passes, lowering, completion/LSP behavior,
  and tests as applicable.
- When changing compiler behavior, run the narrowest relevant tests first and
  then the full verification appropriate to the change.
- Keep performance-sensitive passes structural and typed. Use compiler timings
  before optimizing; avoid repeated whole-program scans or reparsing.

## Verification

Fast iteration:

```powershell
dotnet build Cx.sln
dotnet test tests/Cx.Compiler.Tests/Cx.Compiler.Tests.csproj --no-restore
git diff --check
```

Complete repository gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
```

The complete gate builds Release, runs compiler tests, runs embedded standard
library tests, checks the configured project, audits structured AST usage,
audits generic specialization discovery, and checks diff whitespace.

After compiler changes that must be tested through VS Code, rebuild the bundled
server:

```powershell
dotnet publish src\Cx.Cli\Cx.Cli.csproj -c Release --no-self-contained --output editors\vscode\server
```

Only rebuild/package the extension when the task requires it.
