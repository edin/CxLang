# Tagged unions and matching

A tagged union represents one of several alternatives whose payloads may have
different types. CX stores the active variant explicitly, checks construction
and matching semantically, and lowers the result to a conventional C tag plus
union representation.

The complete example for this chapter is
[`examples/tagged-unions-and-matching.cx`](../../examples/tagged-unions-and-matching.cx).

## Declaring a tagged union

Each variant has a name and exactly one payload type:

```cx
struct Point {
    x: int;
    y: int;
}

union Value {
    Number: int;
    Position: Point;
    Message: const char*;
}
```

Unlike a data enum, variants do not share one metadata schema. `Number` owns an
`int`, `Position` owns a `Point`, and `Message` owns a string pointer. Use a
tagged union when the shape of the stored value changes with the alternative.

## Explicit variant construction

Qualify a variant with its union and call it like a constructor:

```cx
let number: Value = Value.Number(42);
let position: Value = Value.Position(Point { x: 20, y: 22 });
let message: Value = Value.Message("CX");
```

Struct constructor forwarding also works when the variant payload is a struct:

```cx
let position: Value = Value.Position(20, 22);
```

This constructs the `Point` payload from its fields and then wraps it as
`Value.Position`.

Explicit construction is the clearest form when the variant identity matters
to the reader, and it remains unambiguous when multiple variants use the same
payload type.

## Contextual variant conversion

When the target union type is known, CX can wrap a value automatically if
exactly one variant accepts its type:

```cx
let value: Value = 42;       // Value.Number(42)
value = Point { x: 2, y: 3 }; // Value.Position(...)
```

This works in the same expected-type sites as other contextual conversions,
including typed initializers, assignments, arguments, and returns.

The conversion is type-directed. If a union has two `int` variants, an `int`
alone does not identify which tag to use, so write the qualified constructor:

```cx
union Measurement {
    Width: int;
    Height: int;
}

let width = Measurement.Width(10);
```

CX does not guess by variant order.

## Matching and payload bindings

`match` selects an arm by active tag and binds that variant's payload:

```cx
fn score(value: Value*) -> int {
    match value {
        Number: number => {
            return number;
        }
        Position: point => {
            return point.x + point.y;
        }
        Message: text => {
            return text[0] == 'C' ? 42 : 0;
        }
    }
}
```

The binding type comes from the variant declaration:

| Arm | Binding | Inferred type |
| --- | --- | --- |
| `Number` | `number` | `int` |
| `Position` | `point` | `Point` |
| `Message` | `text` | `const char*` |

An arm body may be one statement or a block. Arm bindings are local to their
arms and participate in normal scope and definite-assignment analysis.

Both union values and pointers to union values can be matched. Pointer matching
is convenient when the union should not be copied merely to inspect it.

## Exhaustiveness

A tagged-union match must cover every variant:

```cx
match value {
    Number: number => consume(number);
    Position: point => consume_point(point);
    // error: missing Message
}
```

The diagnostic names the missing variants. CX also diagnoses unknown and
duplicate arms.

An exhaustive match contributes to return-flow analysis, so a function whose
every arm returns does not need an unreachable return after the match.

## Wildcard arms

Use `_` when several alternatives intentionally share one behavior:

```cx
match value {
    Number: number => return number;
    _ => return 0;
}
```

The wildcard makes the match exhaustive and does not bind a payload. Prefer
named arms when variant-specific behavior may be added later; a wildcard will
also absorb variants introduced after it was written.

## Generated C representation

The `Value` declaration lowers conceptually to:

```c
typedef enum {
    Value_Tag_Number,
    Value_Tag_Position,
    Value_Tag_Message
} ValueTag;

typedef struct {
    ValueTag tag;
    union {
        int Number;
        Point Position;
        const char* Message;
    } as;
} Value;
```

Construction initializes both the tag and its matching payload:

```c
(Value){
    .tag = Value_Tag_Number,
    .as.Number = 42
}
```

A `match` lowers to a `switch` on `value.tag`. Each arm first reads the matching
field from `value.as` into its typed binding and then executes the source arm.
No runtime type registry, heap allocation, or hidden object header is required.

## Methods and extensions

Tagged unions participate in the same function catalog as other CX types.
They may own static constructors and behavior, and extensions can add methods
without changing the original declaration. A method can use `match` internally
to provide one operation across every variant.

For example, a `Thing` union can expose `surface()` while each arm obtains the
surface from a differently shaped payload. The union remains responsible for
dispatch, while the generated C remains an ordinary tag switch.

## Raw unions are different

CX also supports untagged C-layout unions explicitly:

```cx
raw union NumberBits {
    integer: u32;
    decimal: float;
}
```

A raw union overlays its fields and does not store which field is active. It is
intended for C interop and low-level representation work. Because there is no
tag to inspect, CX rejects `match` on a raw union.

Choose deliberately:

| Form | Active variant tracked | Pattern matching | Primary use |
| --- | --- | --- | --- |
| `union` | Yes | Yes | Safe alternative values |
| `raw union` | No | No | C ABI and memory overlays |

## Tagged unions versus data enums

The two features solve different modeling problems:

| Question | Data enum | Tagged union |
| --- | --- | --- |
| Does every member share one schema? | Yes | No |
| Is data immutable and shared by member identity? | Yes | No |
| Does each value carry a variant payload? | No | Yes |
| Can variants have different payload types? | No | Yes |
| Is exhaustive payload matching central? | No | Yes |

Use a data enum for a closed catalog such as token kinds with precedence and
spelling. Use a tagged union for a value such as a parse result that is either
an AST node or an error.

## Current boundaries

- Every tagged-union variant declares exactly one payload type.
- A variant constructor currently needs an expected union type, such as the
  annotation in `let value: Value = Value.Number(42)`.
- Contextual wrapping requires exactly one matching payload type.
- Matches must be exhaustive unless they contain `_`.
- A wildcard has no payload binding.
- Raw unions cannot be pattern-matched.
- Variant payloads use value semantics; use pointer payload types when the
  payload should be indirect.

## Related chapters

- [Data types](02-data-types.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
- [Enums and data enums](08-enums-and-data-enums.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)

Interface matching uses similar surface syntax but performs implementation-type
dispatch rather than tagged-union tag dispatch. It will be covered with
interfaces and adapters.
