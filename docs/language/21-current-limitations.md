# Current limitations

CX is an experimental language and compiler. Its typed AST pipeline, native C
backend, compile-time evaluator, macros, generics, resource lowering, modules,
and tooling are substantial enough for real experiments, but neither syntax
nor implementation should yet be treated as production-stable.

This page separates three different kinds of boundary:

- **Language boundaries** are deliberate behavior of the current design.
- **Known implementation issues** are behavior the compiler intends to support
  but does not yet lower correctly in every case.
- **Open design questions** have not been settled and should not be encoded as
  compatibility promises.

Chapter-specific details remain in each guide's `Current boundaries` section.

## Stability and compatibility

CX currently makes no source-, ABI-, or generated-C compatibility promise
between revisions. In particular:

- syntax and standard-library naming may change;
- generated C symbol names may change as module safety and specialization
  improve;
- compile-time reflection and macro APIs may gain or rename properties;
- `cx.toml` fields and CLI output conventions may evolve; and
- adapter, interface, requirement, and ownership rules may be tightened.

The standard library is intentionally small, which makes global naming and API
cleanup practical before compatibility commitments are introduced.

## Memory and ownership safety

CX retains C-shaped value and pointer semantics. It is not currently a
memory-safe language:

- pointer dereference and pointer-range validity are the programmer's
  responsibility;
- array, slice, range, and contiguous indexing does not add runtime bounds
  checks;
- slices, pointer ranges, interfaces, and borrowed views have no static
  lifetime tracking;
- structs containing owning handles can be copied as ordinary values;
- copied resource values can therefore duplicate ownership accidentally; and
- native libraries keep their own allocation, lifetime, thread, and aliasing
  contracts.

`using` provides deterministic scope cleanup, including early exits,
reassignment, and direct return transfer. It is useful ownership machinery,
but it is not a borrow checker:

```cx
using resource = Resource.create();
```

Only `using` bindings receive automatic cleanup. Manual disposal of a `using`
binding can cause double cleanup unless the resource itself guards against it.
Ownership transfer is currently recognized for a direct return of the owned
local; more complex movement should be explicit.

The standard `Disposable<T>` requirement also carries a redundant-looking type
argument in today's model:

```cx
struct Resource: Disposable<Resource> {
}
```

The implicit receiver already identifies `Self`, so a future simplification to
a non-generic `Disposable` is plausible. The current generic spelling remains
the implemented and documented form until that design is changed deliberately.

## Construction and inference boundaries

Named struct initializers carry their type directly:

```cx
let point = Point { x: 1, y: 2 };
```

Some positional construction remains contextual. Call-shaped struct
construction and tagged-union variant construction currently need an expected
type:

```cx
let point: Point = Point(1, 2);
let value: Value = Value.Number(42);
```

Generic inference must receive enough argument or expected-type information to
bind every type parameter. CX does not guess unresolved parameters from names
or declaration order.

An inferred fixed array `T[]` requires a non-empty positional initializer.
Portable C has no general zero-length fixed array for CX to infer from an empty
initializer.

Runtime nullable value types are not generalized through `T?`. Nullable syntax
currently belongs to compile-time values; runtime optionals use pointers,
tagged unions, or `Option<T>`.

## Functions and runtime abstraction

Current function boundaries include:

- function return types are explicit;
- call arguments are positional rather than named;
- function expressions lower to C function pointers and do not capture a
  lexical environment;
- generic functions are compile-time specializations, not erased runtime
  generic values;
- extern functions cannot overload one C ABI symbol with different
  signatures; and
- C variadic arguments receive only fixed-prefix checking.

Interfaces provide runtime function-table dispatch, but interface declarations
do not currently take generic type parameters. Interface values borrow their
concrete state without extending its lifetime. Requirements are separate:
they are structural compile-time contracts and do not create runtime interface
objects.

Adapters preserve and reinterpret one storage representation. They cannot add
independent instance storage, and only explicitly exposed storage methods join
the adapter API.

## Control flow and iteration

`switch` intentionally retains C-style explicit `break` behavior. Tagged-union
`match` must be exhaustive unless it includes `_`; raw C unions cannot be
pattern-matched.

Scalar ranges advance forward by one. Descending ranges and custom steps need
an explicit loop. Reference iteration requires an iterator or contiguous
protocol that can expose stable pointers, and custom iterators must keep those
pointers valid while the loop uses them.

`try` is typed `Result<T, Error>` propagation. The `??` operator is implemented
for lazy `try` fallback chains and is not a general exception or nullable
coalescing mechanism.

## Compile-time programming boundaries

Compile-time execution is intentionally constrained:

- evaluator values must belong to the supported typed compile-time model;
- arbitrary runtime code and objects cannot execute during compilation;
- evaluation has call-depth and step budgets;
- reflection exposes registered semantic objects rather than unrestricted AST
  mutation;
- `@if` requires a Boolean and `@foreach` requires an iterable evaluator value;
- compile-time-only nodes must expand or diagnose before C lowering; and
- generated syntax must pass the normal semantic pipeline.

Attributes are typed metadata, not behavior. They produce no automatic runtime
table and no built-in derive operation. A macro or compile-time consumer must
interpret them. Attribute schema defaults are compile-time expressions; they
cannot depend on runtime state.

Macro result contracts are intentionally distinct. `-> T` returns exactly one
expression, while `elements<T>` returns a sequence for a positional
initializer. Element macros do not currently target named initializer fields,
and an empty element sequence cannot determine the size of a complete inferred
fixed array.

## Modules and names

Modules provide semantic ownership and visibility. Module declarations can
carry attributes that merge across all files contributing to the same logical
module, and compile-time code can inspect them through `module.attributes` and
`module.attribute("name")`. The `program` reflection root exposes projected
modules through `program.modules` and `program.module(name)`; modules outside
the compilation's import graph are intentionally absent.

Source syntax uses dots for both module qualification and member access:

```cx
geometry.core.Point.origin()
```

Resolution consumes the module, type, and member chain semantically, so no
separate module separator is required. Casing is a project convention rather
than grammar; lowercase, PascalCase, and mixed module styles do not change name
resolution.

Other current module boundaries are:

- module blocks cannot be nested;
- file-scoped modules and module blocks cannot be mixed in one file;
- imports are private dependencies rather than re-exports;
- only public declarations cross module boundaries; and
- a public signature cannot expose a private type.

## C interoperability boundaries

CX can model C headers and ABIs, but it does not verify declarations against
the actual native header or binary. A wrong manually written signature,
calling convention, struct layout, ownership rule, format string, or platform
width remains an ABI error.

Opaque C types expose identity but no CX-visible size or fields. Variadic calls
only validate fixed parameters. Arbitrary preprocessor evaluation remains the
job of the downstream C compiler; CX requires typed `macro const` and
`macro fn` declarations for macros it uses.

Native compilation depends on an external C toolchain. The CLI defaults to
GCC, supports executable and shared-library project kinds, and currently
recognizes `windows`, `linux`, and `macos` for conditional link declarations.

## Tooling boundaries

The CLI uses the fixed `cx.toml` default project name. Shared projects require
explicit entry points and cannot be launched directly.

Compiler commands expose shared phase timing reports through `--timings`. The
VS Code extension focuses on syntax highlighting, diagnostics, and member
completion; CX does not yet offer a complete refactoring, debugging,
package-management, formatting, or documentation-generation toolchain.

## Known implementation issues

The following are implementation defects or incomplete lowering paths rather
than intended language restrictions.

### Cross-source module-generation placement

The PHP extension currently keeps its file-level `use PhpModule();` in
`php_binding.cx`. Moving the invocation into `main.cx` caused the configured
`get_module` entry point to disappear from lookup, even with the relevant
helpers made public and source order controlled.

Current workaround: keep the macro invocation in the binding file. The likely
fault area is cross-source macro expansion, declaration ownership, or module
visibility.

## Areas needing focused design or regression work

These items are not yet specified strongly enough to call them permanent
limitations or confirmed defects:

- decide whether the standard style uses lowercase or PascalCase modules and
  free functions, without making casing semantic;
- evaluate simplifying `Disposable<T>` to `Disposable` with implicit `Self`;
- strengthen generic static-factory and `Self` specialization coverage;
- move PHP binding composition into an application or dedicated composition
  module after cross-source generation is fixed; and
- stabilize compile-time reflection names before treating them as a public
  compatibility surface.

## Production-readiness summary

CX is appropriate today for compiler development, language experiments,
generated-C inspection, small native programs, and controlled C integration.
It should not yet be used where memory safety, stable language compatibility,
cross-platform ABI guarantees, a mature package ecosystem, or production IDE
support is required.

The strongest current path is a small project with reviewed C declarations,
explicit ownership, focused native tests, inspected generated C, and a pinned
compiler revision.

## Related chapters

- [Data types](02-data-types.md)
- [Resource management](14-resource-management.md)
- [Modules and visibility](15-modules-and-visibility.md)
- [Compile-time programming](17-compile-time-programming.md)
- [Macros](18-macros.md)
- [C interoperability](19-c-interop.md)
- [Projects, building, and testing](20-projects-building-and-testing.md)
