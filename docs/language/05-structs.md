# Structs

CX structs are typed, C-friendly value aggregates. They support direct field
layout, several initializer forms, methods and static functions, generic
specialization, structural requirement declarations, pointers, and ordinary
value copying without requiring a hidden runtime object model.

The complete example for this chapter is
[`examples/structs-guide.cx`](../../examples/structs-guide.cx).

## Declaring a struct

A struct lists named fields and their types:

```cx
struct Point {
    x: int;
    y: int;
}
```

Fields may use any representable CX type, including other structs, pointers,
fixed arrays, function types, enums, interfaces, and specialized generic types:

```cx
struct Line {
    start: Point;
    end: Point;
}

struct Buffer {
    data: u8*;
    length: usize;
    inline_bytes: u8[16];
}
```

The declaration owns the field order used by the generated C struct.

## Named initialization

Named initializers make field correspondence explicit:

```cx
let point = Point {
    x: 10,
    y: 20
};
```

CX validates field names and value types. Unknown and duplicate fields are
diagnosed before C emission.

When the target type is already known, omit the repeated type name:

```cx
let point: Point = {
    x: 10,
    y: 20
};
```

This contextual form is particularly useful for return values, assignments,
and nested aggregates.

## Nested initialization

Expected field types flow into nested initializer blocks:

```cx
let line: Line = {
    start: { x: 1, y: 2 },
    end: { x: 3, y: 4 }
};
```

The inner blocks are typed as `Point` from `Line.start` and `Line.end`. CX keeps
them as structured initializer AST nodes through semantic analysis and C
lowering; it does not turn them into source text and parse them again.

## Positional construction

A struct name may be called with values in field declaration order:

```cx
let point: Point = Point(10, 20);
```

This is constructor syntax for the aggregate, not a hidden user-defined
constructor. It lowers to a typed struct initializer. The call-shaped form
currently needs an expected struct type, such as the local annotation above.
Named initialization is usually clearer for large structs or adjacent fields
of the same type.

The dedicated
[constructors, initializers, and typed AST macros guide](../features/initializers-and-typed-macros.md)
covers positional and named construction, contextual blocks, inferred arrays,
initializer splicing, and macro-generated elements in detail.

## Reading and writing fields

Use member access to read or assign fields:

```cx
let point: Point = Point(10, 20);
let x = point.x;
point.y = 42;
```

`const` bindings reject mutation through the binding:

```cx
const origin = Point(0, 0);
origin.x = 1; // error: origin is const
```

Field assignment still undergoes normal type compatibility and implicit
conversion checks.

## Value semantics

A struct value is copied when assigned or passed by value:

```cx
let first = Point(1, 2);
let second = first;
second.x = 10;
```

Changing `second` does not change `first`. This matches ordinary C struct value
semantics. For large objects, shared mutation, or stable identity, use a
pointer explicitly.

## Struct pointers

Take a field-owning value's address with `&` and accept it as `Point*`:

```cx
fn move_right(point: Point*) -> void {
    point.x += 1;
}

move_right(&point);
```

CX uses the same member syntax for struct values and pointers. The C backend
chooses `.` or `->` from the resolved receiver type:

```cx
let x = point.x; // works when point is Point or Point*
```

Dereference explicitly with `*` when a complete value is required:

```cx
let copy: Point = *pointer;
```

Pointers remain explicit and may be `null`; CX does not insert automatic null
checks.

## Instance methods

Methods may be declared inside the struct:

```cx
struct Point {
    x: int;
    y: int;

    fn translate(dx: int, dy: int) -> void {
        self.x += dx;
        self.y += dy;
    }
}

point.translate(2, 3);
```

When no receiver is written, CX supplies an implicit `self` receiver. The body
uses `self` explicitly, and the call lowers to an ordinary C function receiving
the struct address.

An explicit receiver remains available when its exact type or pointer shape is
part of the declaration:

```cx
fn length(self: Point*) -> int {
    return self.x * self.x + self.y * self.y;
}
```

Methods can also be declared with a qualified name or in an extension block.
See [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md).

## Static functions and factories

A static function belongs to the type but has no instance receiver:

```cx
struct Point {
    x: int;
    y: int;

    static fn origin() -> Point {
        return Point { x: 0, y: 0 };
    }
}

let origin = Point.origin();
```

Static factories are ordinary typed functions. Names such as `create`,
`empty`, and `from` are conventions rather than reserved constructor hooks.
They may validate input, allocate resources, or select an initialization policy
before returning a value.

## Self type

Inside a qualified method declaration, `Self` refers to the owning concrete
type:

```cx
struct Counter {
    value: int;
}

fn Counter.copy(self: Self*) -> Self {
    let other: Self;
    other.value = self.value;
    return other;
}
```

Here every `Self` resolves to `Counter`. This keeps receiver-oriented behavior
stable when the concrete declaration name changes. Requirements also use
`Self` to describe the eventual providing type.

## Generic structs

Type parameters follow the struct name:

```cx
struct Pair<T> {
    first: T;
    second: T;

    static fn create(first: T, second: T) -> Pair<T> {
        return Pair<T> { first: first, second: second };
    }
}

let pair: Pair<int> = Pair<int> {
    first: 20,
    second: 22
};
```

CX specializes generic structs for the concrete types used by the program.
`Pair<int>` and `Pair<double>` become distinct typed C declarations with
deterministic names. Their methods are specialized consistently with their
receiver types.

Generic requirements can restrict which types are valid for a parameter. Their
full matching and specialization rules belong to the planned generics chapter.

## Declaring structural requirements

A struct can declare that it provides one or more requirements:

```cx
requires Resettable {
    fn reset() -> void;
}

struct Counter: Resettable {
    value: int;

    fn reset() -> void {
        self.value = 0;
    }
}
```

The colon does not mean C++ class inheritance. It declares semantic
capabilities that generic code and compiler protocols may require. CX checks
requirement names, type arguments, fields, methods, static functions, and
operator signatures as applicable.

Like an owned instance method, a non-static requirement function receives an
implicit `self: Self*` receiver. The declaration above is therefore equivalent
to the explicit long form:

```cx
requires Resettable {
    fn reset(self: Self*) -> void;
}
```

The shorter form is usually preferable. Write the receiver explicitly when its
presence or exact pointer shape is important to the requirement's explanation.

A declaration can provide several requirements:

```cx
struct Collection<T>: Contiguous<T>, Disposable<Collection<T>> {
    // required representation and behavior
}
```

Requirements do not inject storage into the struct. They describe facts that
the declaration must satisfy structurally.

## Generated C representation

A simple CX declaration remains recognizable in C:

```cx
struct Point {
    x: int;
    y: int;
}
```

Conceptually lowers to:

```c
typedef struct Point {
    int x;
    int y;
} Point;
```

Methods lower separately:

```c
void Point_translate(Point* self, int dx, int dy);
```

CX may specialize names, order declarations to satisfy dependencies, and prune
unreachable declarations, but it does not add a virtual table or object header
to an ordinary struct.

## Structs and C interop

Ordinary CX structs are emitted as C structs and are useful for transparent
data modeling. When an ABI declaration must exactly mirror an external header,
use `declare c` forms so the compiler treats the declaration as C-owned.
Opaque and forward-declared C types also belong there rather than in an empty
CX struct used as a workaround.

Exact ABI compatibility still depends on field order, field types, target
alignment, packing expectations, and the compiler toolchain.

## Compile-time reflection

Struct declarations can be inspected by compile-time functions and macros.
Reflection exposes fields, their resolved types and attributes, methods,
visibility, generic parameters, and declared requirements:

```cx
@foreach field in Target.fields {
    generate_for(field);
}
```

This is the foundation used by generated serializers, debug writers, foreign
function tables, and other typed AST macros.

## Current boundaries

- Structs use value semantics unless a pointer is written explicitly.
- Ordinary structs do not carry hidden runtime type information.
- Pointer dereference safety remains the programmer's responsibility.
- Positional construction follows declaration order and therefore couples the
  call to that order.
- Call-shaped positional construction currently needs an expected struct type;
  named `Point { ... }` initialization carries its type directly.
- Static factory names have no privileged language behavior unless explicitly
  marked for a feature such as implicit conversion.
- Requirement declarations describe structural capabilities, not storage or
  implementation inheritance.

## Related chapters

- [Data types](02-data-types.md)
- [Variables and constants](03-variables-and-constants.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Tagged unions and matching](09-tagged-unions-and-matching.md)
