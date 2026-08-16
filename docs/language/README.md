# CX Language Guide

This guide documents language behavior that is implemented in the current CX
compiler. CX remains experimental: the guide describes what works today, not a
stability promise for future releases.

## Learning path

Start with the foundational chapters and continue toward the compile-time
system:

1. [Data types](02-data-types.md)
2. [Variables and constants](03-variables-and-constants.md)
3. [Expressions and operators](04-expressions-and-operators.md)
4. [Structs](05-structs.md)
5. [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
6. [Functions and overloads](06-functions-and-overloads.md)
7. [Control flow](07-control-flow.md)
8. [Enums and data enums](08-enums-and-data-enums.md)
9. [Tagged unions and matching](09-tagged-unions-and-matching.md)
10. [Arrays, slices, and iteration](10-arrays-slices-and-iteration.md)
11. [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
12. [Generics and requirements](12-generics-and-requirements.md)
13. [Interfaces and adapters](13-interfaces-and-adapters.md)
14. [Resource management](14-resource-management.md)
15. [Modules and visibility](15-modules-and-visibility.md)
16. [Attributes and reflection](16-attributes-and-reflection.md)
17. [Compile-time programming](17-compile-time-programming.md)
18. [Macros](18-macros.md)
19. [C interoperability](19-c-interop.md)
20. [Projects, building, and testing](20-projects-building-and-testing.md)
21. [Current limitations](21-current-limitations.md)

The main feature-guide round is now complete. Each chapter was written from
compiler tests and working examples rather than inferred from surface syntax
alone. A dedicated getting-started tutorial can be added separately; the root
README and the projects chapter currently provide that workflow.

## Implemented feature inventory

| Area | Implemented behavior | Guide status |
| --- | --- | --- |
| Data types | Primitive C-facing types, pointers, `const`, nullable compile-time values, fixed arrays, generics, function types, aliases | [Documented](02-data-types.md) |
| Bindings | Global and local `let`, `const`, inference, assignment checking, compile-time constants, `using` cleanup bindings | [Documented](03-variables-and-constants.md) |
| Expressions | Literals, calls, members, indexing, casts, `sizeof`, arithmetic, comparison, bitwise/logical operators, conditionals, ranges, lambdas, assignment | [Documented](04-expressions-and-operators.md) |
| Construction | Struct and tagged-union constructors; named, positional, contextual, and nested initializers | [Documented](../features/initializers-and-typed-macros.md) |
| Arrays and iteration | Fixed and inferred arrays, slices, pointer ranges, vectors, scalar ranges, contiguous and custom iterator protocols, key/value iteration | [Documented](10-arrays-slices-and-iteration.md) |
| Functions | Free functions, overloads, methods, static methods, generic calls, function expressions, function values, extern and variadic functions | [Documented](06-functions-and-overloads.md) |
| Control flow | `if`/`else`, `while`, `for`, `foreach`, ranges, `switch`, `match`, `break`, `continue`, `return`, `try` propagation and fallback | [Documented](07-control-flow.md) |
| Data declarations | Structs, ordinary enums, data enums, tagged unions, opaque C declarations | [Structs](05-structs.md); [enums and data enums](08-enums-and-data-enums.md); [tagged and raw unions](09-tagged-unions-and-matching.md) documented |
| Extensions and conversions | Owned and qualified methods, generic and constrained extensions, operator declarations and derivation, implicit factories | [Documented](11-methods-extensions-operators-and-conversions.md) |
| Generics | Generic functions and types, specialization, `where` constraints, structural requirements | [Documented](12-generics-and-requirements.md) |
| Interfaces and adapters | C-friendly interface values, function-pointer tables, implementation matching, storage adapters, exposed and retargeted methods | [Documented](13-interfaces-and-adapters.md) |
| Resources | `using`, deterministic reverse-order cleanup, early-exit cleanup, reassignment cleanup, return transfer, `try` integration | [Documented](14-resource-management.md) |
| Iteration | Arrays, ranges, contiguous values, iterators, indexed iteration, key/value iteration, const/reference bindings | [Documented](10-arrays-slices-and-iteration.md) |
| Modules | Imports and aliases, module blocks, file modules, public API validation, module-aware semantics and generated names | [Documented](15-modules-and-visibility.md) |
| Attributes | Typed attributes on declarations and parameters, metadata lookup, constructed attributes | [Documented](16-attributes-and-reflection.md) |
| Compile time | Compile functions/constants, lists, reflection objects, intrinsics, diagnostics, `@let`, `@if`, and `@foreach` | [Documented](17-compile-time-programming.md) |
| Macros | Statement/declaration macros, typed expression results, `elements<T>`, initializer directives and splicing | [Documented](18-macros.md) |
| C interop | `extern`, typed `declare` blocks, includes, links, aliases, pointers, function pointers, header-free ABI declarations | [Documented](19-c-interop.md) |
| Projects and tools | Executable/shared projects, configured entry points, build/run/test, generated C, language server | [Documented](20-projects-building-and-testing.md) |

## Chapter map

```text
01-getting-started.md
02-data-types.md
03-variables-and-constants.md
04-expressions-and-operators.md
05-structs.md
06-functions-and-overloads.md
07-control-flow.md
08-enums-and-data-enums.md
09-tagged-unions-and-matching.md
10-arrays-slices-and-iteration.md
11-methods-extensions-operators-and-conversions.md
12-generics-and-requirements.md
13-interfaces-and-adapters.md
14-resource-management.md
15-modules-and-visibility.md
16-attributes-and-reflection.md
17-compile-time-programming.md
18-macros.md
19-c-interop.md
20-projects-building-and-testing.md
21-current-limitations.md
```

The [combined initializer guide](../features/initializers-and-typed-macros.md)
remains the detailed account of inferred arrays and typed initializer macros;
the dedicated [macro chapter](18-macros.md) places those contracts in the full
macro system.

## Documentation policy

Every completed chapter should include:

1. the smallest useful CX example;
2. semantic rules rather than syntax alone;
3. generated C when it clarifies the model;
4. invalid examples and diagnostics where important;
5. current limitations and non-goals;
6. links to related chapters and regression tests.

Features that are only proposed belong under
[`docs/ideas`](../ideas/README.md), not in this inventory. The limitations
chapter explicitly labels unsettled syntax separately from implemented CX.
