# Enums and data enums

CX has two related enum forms. An ordinary enum is a compact set of named
integer values. A data enum gives every member the same typed, immutable
metadata schema while keeping the member itself an enum value.

The complete example for this chapter is
[`examples/enums-and-data-enums.cx`](../../examples/enums-and-data-enums.cx).

## Ordinary enums

Declare an enum with a name and a list of members:

```cx
enum Color {
    Red,
    Green,
    Blue,
}
```

Members are accessed through their enum type in expressions:

```cx
let color: Color = Color.Green;
```

Different enums may use the same member names. `Color.Value` and
`State.Value` remain distinct symbols, and their generated C names are
qualified by their owning enums.

## Explicit values

Members may specify their underlying integer value:

```cx
enum Status {
    Pending,
    Running = 10,
    Finished,
}
```

CX preserves C-style enum value expressions, including balanced expressions
such as shifts and parenthesized combinations. Cast explicitly when the
numeric representation is part of the operation:

```cx
let code: int = (int)Status.Running;
```

Enums provide intrinsic equality and ordering comparisons. They do not need
user-defined operators to satisfy generic equality or comparison requirements.

## Switching on an enum

Enums work naturally with `switch`:

```cx
fn color_weight(color: Color) -> int {
    switch (color) {
        case Color.Red: {
            return 10;
        }
        case Color.Green: {
            return 20;
        }
        default: {
            return 0;
        }
    }
}
```

Qualifying case labels keeps their enum ownership explicit and produces
module-safe generated C names.

## Data enum declarations

A parameter list after the enum name defines a shared metadata schema. Each
member then supplies a named initializer for that schema:

```cx
enum TokenKind(
    text: const char* = null,
    precedence: int = 0
) {
    Identifier {},
    Plus { text: "+", precedence: 90 },
}
```

The members are still enum values:

```cx
let kind: TokenKind = TokenKind.Plus;
```

Metadata is read directly from the value:

```cx
let precedence: int = kind.precedence;
let spelling: const char* = kind.text;
```

Every member has every field. A member initializer may override a field, while
omitted fields use their declared defaults. A field without a default is
required for every member.

The compiler reports unknown fields, duplicate field values, and missing
required fields at the declaration rather than leaving an incomplete table for
the C compiler.

## Contextual defaults

Defaults can depend on the member currently being materialized:

```cx
enum TokenKind(
    name: const char* = member.name,
    index: int = member.index
) {
    Identifier {},
    Number {},
    Plus { index: 10 },
}
```

`member.name` is the member name as a string, and `member.index` is its
zero-based declaration index. The example produces metadata equivalent to:

```text
Identifier: { name: "Identifier", index: 0 }
Number:     { name: "Number",     index: 1 }
Plus:       { name: "Plus",       index: 10 }
```

The `member` context exists only inside data-enum field default expressions.
Its supported properties are `name` and `index`.

## Metadata is immutable

Data-enum metadata describes a member rather than state owned by one variable.
It therefore cannot be assigned through a value:

```cx
let kind = TokenKind.Plus;
kind.precedence = 10; // error: enum metadata is immutable
```

Two variables holding `TokenKind.Plus` always observe the same metadata.

## The generated C model

A data enum lowers to an ordinary C enum, a data struct, and a static constant
table indexed by the enum value. Conceptually:

```c
typedef enum TokenKind {
    TokenKind_Identifier,
    TokenKind_Plus,
    TokenKind_COUNT
} TokenKind;

typedef struct TokenKind_Data {
    const char* text;
    int precedence;
} TokenKind_Data;

static const TokenKind_Data TokenKind_data[TokenKind_COUNT] = {
    [TokenKind_Identifier] = { .text = NULL, .precedence = 0 },
    [TokenKind_Plus] = { .text = "+", .precedence = 90 }
};
```

An access such as `kind.precedence` becomes
`TokenKind_data[kind].precedence`. The enum value stays small, copying it does
not copy the metadata, and the generated representation remains readable C.

## Function-valued metadata

Metadata fields can use function types and nullable defaults:

```cx
fn increment(value: int) -> int {
    return value + 1;
}

enum Operation(handler: fn(int) -> int = null) {
    None {},
    Increment { handler: increment },
}

let operation = Operation.Increment;
if (operation.handler != null) {
    let answer = operation.handler(41);
}
```

Functions referenced only from a data-enum initializer are retained when CX
prunes unreachable generated C declarations.

## Runtime iteration

Iterating the enum type visits all members in declaration order:

```cx
foreach index, kind in TokenKind {
    consume(index, kind, kind.precedence);
}
```

The optional first binding is the zero-based iteration index; the second is the
enum member value. Enum members are values backed by shared metadata, so the
member binding cannot be taken by reference.

## Compile-time reflection

Enum declarations are also available to compile-time code:

```cx
@foreach member in TokenKind.members {
    generate_for(@{member.value});
}
```

Reflection exposes whether a type is an enum or data enum, its members and
their indexes, the data-field schema, and each member's effective metadata.
Effective metadata distinguishes explicit values, defaulted values, and null.
This makes data enums useful as a typed source for generated declarations and
tables—not merely as runtime lookup tables.

## Enum extensions

Ordinary and data enums can receive extension methods:

```cx
extension TokenKind {
    fn token_text() -> const char* {
        return self.text;
    }
}

let text = TokenKind.Plus.token_text();
```

Within a data-enum extension, metadata access through `self` uses the same
typed table lookup as access through a local enum value.

## Current boundaries

- A data enum must declare at least one metadata field; `enum Empty()` is
  rejected.
- All members share one schema. Use a tagged union when variants need different
  payload shapes.
- Metadata is immutable and shared by member identity.
- Contextual `member` access is limited to data-field defaults.
- Runtime reference bindings in data-enum iteration are rejected.

## Related chapters

- [Data types](02-data-types.md)
- [Expressions and operators](04-expressions-and-operators.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)

Tagged unions and `match` are covered in
[Tagged unions and matching](09-tagged-unions-and-matching.md).
