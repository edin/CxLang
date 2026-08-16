# Variables and constants

CX has mutable `let` bindings, immutable `const` bindings, deterministic
cleanup bindings with `using`, and a separate compile-time constant form.

## Local `let` bindings

`let` introduces a mutable local:

```cx
fn main() -> int {
    let count: int = 1;
    count = count + 1;
    return count;
}
```

The type may be inferred from an initializer:

```cx
let count = 42;                       // int
let ratio = 0.5;                      // double
let point = Point { x: 10, y: 20 };  // Point
```

A declaration may omit its initializer when its type is explicit:

```cx
let buffer: Buffer;
```

Reading an uninitialized value retains the same low-level risks as C; CX does
not currently provide definite-assignment guarantees for every path.

## Local `const` bindings

`const` introduces a binding that cannot be reassigned:

```cx
const limit: int = 100;
```

Mutation through the const value is also rejected for ordinary aggregate
members:

```cx
const box: Box = Box { value: 1 };
box.value = 2; // diagnostic
```

`const` requires an initializer. It is a runtime declaration unless combined
with `compile` at declaration scope.

## Global bindings

Globals use the same `let` and `const` spellings:

```cx
const scale: int = 2;
let calls: int = 0;

fn next() -> int {
    calls += 1;
    return calls * scale;
}
```

Global initializers must be lowerable to valid static C initialization. The
compiler emits declarations before globals whose initializers depend on
function addresses or other declared symbols.

## Binding type versus value type

These declarations describe different constraints:

```cx
let first: const char* = "text";
const second: char* = obtain_mutable_text();
const third: const char* = "text";
```

- `first` may be rebound, but characters are viewed through a const-qualified
  pointer type.
- `second` cannot be rebound, while its pointer type is not const-qualified.
- `third` combines both restrictions.

## Assignment compatibility

Assignments and initializers are checked structurally:

```cx
let value: i64 = 42;
let pointer: int* = null;
let operation: fn(int, int) -> int = add;
let numbers: Vec<int> = Vec<int>.create();
```

Compatibility accounts for primitive conversions, pointers and `null`, array
shape, aliases, concrete generic arguments, function signatures, interfaces,
and adapters. Incompatible values receive a source diagnostic before C
lowering.

Compound assignment is available for arithmetic operations:

```cx
count += 1;
count -= 1;
count *= 2;
count /= 2;
count %= 10;
```

## Fixed-array inference

An explicit element type with `[]` infers its fixed length from the
initializer:

```cx
let offsets: int[] = { 4, 8 };
const codes: u8[] = { 10, 20, 30 };
```

Typed element macros can also provide the initializer and, when used as the
complete expression, the entire array type:

```cx
macro Values() -> elements<int> {
    return { 10, 20, 30 };
}

const values = use Values(); // int[3]
```

See [Element-sequence macros](../features/initializers-and-typed-macros.md#element-sequence-macros).

## `using` cleanup bindings

`using` declares a local whose value is deterministically disposed when its
scope exits:

```cx
fn write_message() -> void {
    using builder: StringBuilder = StringBuilder.create();
    builder.append_cstr("hello");
}
```

Cleanup is control-flow-sensitive:

- bindings are cleaned in reverse declaration order;
- early `return`, `break`, and `continue` paths receive required cleanup;
- replacing an owned value cleans the previous value in the correct order;
- directly returning an owned binding transfers it rather than cleaning it on
  that return path.

The type must provide the cleanup behavior required by the resource-lowering
pipeline. `using` is not equivalent to C# `using` syntax and does not imply a
garbage collector.

## Compile-time constants

`compile const` creates a typed value evaluated by the compile-time system:

```cx
compile const api_prefix: string = "/api";
compile const enabled: bool = true;
compile const names: list<string> = ["first", "second"];
```

Compile-time constants:

- require an explicit compile-time type and initializer;
- may reference earlier compile-time constants;
- may be public where module visibility permits;
- are available to compile-time directives, functions, reflection, and macros;
- must be fully evaluated and removed before runtime lowering.

They are separate from runtime `const` globals:

```cx
const runtime_limit: int = 100;
compile const generated_name: string = "limit";
```

## Macro-generated bindings

Macro templates may compute local binding names:

```cx
let @{as_name(parameter.name)}: int = 0;
```

The placeholder produces a structured name during expansion. It is not pasted
as source text. Computed-name support is intentionally limited to syntax
positions registered by the macro system; it should not be assumed for every
possible declaration position.

## Related guides

- [Data types](02-data-types.md)
- [Expressions and operators](04-expressions-and-operators.md)
- [Initializers and typed AST macros](../features/initializers-and-typed-macros.md)
