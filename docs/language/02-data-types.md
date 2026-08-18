# Data types

CX uses structured types that map predictably to C while remaining available
to semantic analysis, generics, reflection, and macros.

## Primitive types

The compiler knows the following primitive runtime types directly.

| Category | CX types |
| --- | --- |
| No value | `void` |
| Boolean | `bool` |
| Character | `char` |
| C signed integers | `signed char`, `short`, `int`, `long`, `long long` |
| C unsigned integers | `unsigned char`, `unsigned short`, `unsigned int`, `unsigned long`, `unsigned long long` |
| Fixed-width signed integers | `i8`, `i16`, `i32`, `i64` |
| Fixed-width unsigned integers | `u8`, `u16`, `u32`, `u64` |
| Size type | `usize` |
| Floating point | `float`, `double`, `long double` |

The C-style integer types preserve the target C implementation's model where
their width is platform-dependent. The `iN` and `uN` spellings request the
corresponding fixed width.

Examples:

```cx
let count: int = 42;
let bytes: usize = 1024;
let identifier: u64 = 100;
let ratio: double = 0.5;
let enabled: bool = true;
let separator: char = ':';
```

Integer literals initially have type `int`, and floating-point literals
initially have type `double`. Assignment and call checking apply the primitive
conversion rules, including range checks where a literal is converted to a
narrower integer type.

## Strings

A runtime string literal has type `char*` and lowers to a C string literal:

```cx
let message: const char* = "hello";
```

Raw triple-quoted strings preserve their contents exactly. They do not process
escape sequences, interpolation, or indentation:

```cx
let template: const char* = """
Quotes: "hello"
Backslash text: \n
""";
```

The opening and closing delimiters are not part of the value. A raw string
cannot directly contain the closing `"""` sequence; use an ordinary string or
concatenation when that delimiter is required.

`StringView` and `StringBuilder` are standard-library types rather than
language primitives:

```cx
let view: StringView = StringView.from_cstr("hello");
let builder: StringBuilder = StringBuilder.create();
```

Compile-time functions also use a compile-time `string` type. That type belongs
to the compile-time value system and should not be confused with a runtime
owning string representation.

## Pointers and `null`

Pointer types use postfix `*`:

```cx
let value: int = 42;
let pointer: int* = &value;
let missing: void* = null;
```

The ordinary C pointer operators are available:

```cx
let copy: int = *pointer;
pointer = &copy;
```

`null` is compatible with pointer-shaped runtime values and with nullable
compile-time values where permitted by their declared compile-time type.

## Const-qualified types

`const` may qualify a type:

```cx
fn print(text: const char*) -> void {
    // text points to characters that this function does not modify.
}
```

This is distinct from a `const` binding. A type qualifier describes operations
through the value, while a binding qualifier prevents reassignment of the
binding itself. They may be combined:

```cx
const greeting: const char* = "hello";
```

## Fixed arrays

A fixed array includes its length in the type:

```cx
let values: int[3] = { 10, 20, 30 };
```

The length may be an integer or a supported symbolic compile-time size. An
omitted length requests inference from a non-empty positional initializer:

```cx
const values: int[] = { 10, 20, 30 }; // resolves to int[3]
```

See [Inferred fixed-array lengths](../features/initializers-and-typed-macros.md#inferred-fixed-array-lengths)
for the complete rules.

## Function types

Functions are first-class C-compatible values:

```cx
fn add(left: int, right: int) -> int {
    return left + right;
}

let operation: fn(int, int) -> int = add;
let result: int = operation(20, 22);
```

Function types may appear in parameters and return positions. Variadic
function types place `...` last:

```cx
type PrintFunction = fn(const char*, ...) -> int;
```

CX also supports function expressions with expression or block bodies:

```cx
let compare: fn(int, int) -> int =
    fn(left: int, right: int) => left <=> right;
```

Current function expressions are lowered to C functions; they are not general
capturing closures.

## Named and generic types

Structs, enums, unions, interfaces, and adapters introduce named types:

```cx
struct Point {
    x: int;
    y: int;
}
```

Generic types use angle brackets:

```cx
let numbers: Vec<int> = Vec<int>.create();
let names: HashMap<StringView, int> = HashMap<StringView, int>.create();
```

Concrete generic uses are specialized into deterministic C declarations. The
compiler retains structured type arguments rather than treating a type such as
`Vec<int>` as an opaque string.

## Type aliases and adapters

A direct alias gives another name to the same underlying type:

```cx
type String = char*;
type IntList = Vec<int>;
```

An adapter is a distinct CX type layered over a storage type and can expose or
add selected behavior:

```cx
type ByteBuffer using Vec<u8> {
    // Adapter methods and exposed storage behavior.
}
```

Aliases and adapters have different identity and semantic behavior. A direct
alias remains assignment-compatible with its target; an adapter participates
through its declared adapter rules.

## Nullable compile-time types

The `T?` syntax is currently used by the compile-time type system for values
that may be `null`:

```cx
compile fn optional_name(enabled: bool) -> string? {
    if (!enabled) {
        return null;
    }

    return "enabled";
}
```

This should not be read as a general runtime `Option<T>` feature. Runtime
optional values are normally represented with pointers, tagged unions, or the
standard-library `Option<T>` type.

## Computed types in macros

Macro templates may construct a type through a compile-time placeholder:

```cx
let values: @{Type.array(int, 4)} = { 1, 2, 3, 4 };
```

The placeholder must evaluate to a structured compile-time type. Expansion
replaces the computed type before ordinary semantic resolution and C lowering.

## Related guides

- [Variables and constants](03-variables-and-constants.md)
- [Expressions and operators](04-expressions-and-operators.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
