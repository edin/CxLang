# Expressions and operators

For user-defined operators, derived comparisons, and their interaction with
generic requirements, see
[Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md).

CX expressions are represented as typed syntax nodes and resolved before they
reach the C backend. This chapter covers the currently implemented expression
forms and intrinsic operators.

## Literals

```cx
42              // integer
3.14            // floating point
'x'             // character
"hello"         // C string
true            // boolean
false           // boolean
null            // null value
```

List expressions use square brackets in compile-time code:

```cx
compile const names: list<string> = ["first", "second"];
```

Brace expressions are structured initializers, covered in
[Constructors and initializers](../features/initializers-and-typed-macros.md#struct-construction).

## Names, members, and indexing

```cx
value
point.x
buffer.data[index]
matrix[row][column]
```

Member resolution understands fields, methods, static methods, extensions,
interfaces, adapters, reflected compile-time objects, and overload sets as
appropriate to the current compilation phase.

## Function and method calls

```cx
add(20, 22)
builder.append("hello")
Vec<int>.create()
identity<int>(42)
```

Calls are resolved through the shared function catalog. Overloads and generic
specializations are selected from argument and type information rather than by
taking the first declaration with a matching name.

Struct and tagged-union construction also use call syntax:

```cx
let point = Point(10, 20);
let value: Value = Value.Position(10, 20);
```

## Arithmetic operators

```cx
left + right
left - right
left * right
left / right
left % right
```

Unary numeric operators are also supported:

```cx
+value
-value
```

Primitive arithmetic follows CX's intrinsic primitive semantics. User types
may provide supported operator declarations through methods/extensions where
the operator system allows it.

## Comparison operators

```cx
left == right
left != right
left < right
left <= right
left > right
left >= right
left <=> right
```

The spaceship operator `<=>` produces an integer comparison result suitable
for sorting and comparator functions:

```cx
let compare: fn(int, int) -> int =
    fn(left: int, right: int) => left <=> right;
```

Ordinary relational and equality operators produce `bool`.

## Logical and bitwise operators

Logical operators:

```cx
!enabled
left && right
left || right
```

Bitwise operators:

```cx
~bits
left & right
left ^ right
left | right
value << count
value >> count
```

`&&` and `||` retain short-circuit behavior when lowered to C.

## Pointer operators

```cx
let pointer: int* = &value;
let copy: int = *pointer;
```

The same `&` and `*` tokens are disambiguated structurally as prefix address
and dereference operators or as binary bitwise-and and multiplication
operators.

## Increment, decrement, and assignment

Prefix and postfix increment/decrement are distinct expressions:

```cx
++index
--index
index++
index--
```

Assignment is an expression form and includes arithmetic compound assignment:

```cx
value = replacement
value += amount
value -= amount
value *= factor
value /= divisor
value %= modulus
```

Assignment checks mutability and type compatibility before lowering. Resource
bindings may require additional cleanup logic around reassignment.

## Conditional expressions

The ternary expression has the familiar C shape:

```cx
let maximum: int = left > right ? left : right;
```

The condition must be boolean-compatible, and the two result branches must
resolve to a compatible result type.

## Casts

Explicit casts place the target type in parentheses:

```cx
let integer: int = (int)floating_value;
let pointer: Header* = (Header*)raw_memory;
```

Casts are represented with a structured target `TypeNode`; the compiler does
not recover the type by parsing emitted text.

## `sizeof`

`sizeof` accepts either a type or an expression:

```cx
let point_size: usize = sizeof(Point);
let value_size: usize = sizeof(value);
let callback_size: usize = sizeof(fn(int) -> bool);
```

A single identifier may initially be syntactically ambiguous. Semantic
resolution decides whether it names a type or a runtime expression.

## Ranges

CX has exclusive and inclusive scalar range expressions:

```cx
0..10   // excludes 10
0...10  // includes 10
```

They are primarily consumed by `foreach` and range lowering:

```cx
foreach index in 0..10 {
    consume(index);
}
```

## Function expressions

Expression-bodied function values:

```cx
let ascending: fn(int, int) -> int =
    fn(left: int, right: int) => left <=> right;
```

Block-bodied function values:

```cx
let absolute_compare: fn(int, int) -> int =
    fn(left: int, right: int) -> int {
        if (left < 0) {
            left = -left;
        }
        if (right < 0) {
            right = -right;
        }
        return left <=> right;
    };
```

These lower to generated C functions. General lexical capture is not currently
part of the function-expression model.

## `try` expressions

CX integrates standard-library result propagation with `try`:

```cx
let value = try operation();
```

A fallback chain uses `??` after a `try` expression:

```cx
let value = try primary()
    ?? try secondary()
    ?? fallback_value;
```

Fallbacks are evaluated lazily, and nested chains are lowered before the final
semantic and C-lowering stages. `??` is part of this `try` fallback form; it is
not documented as a general-purpose nullable coalescing operator.

## Macro invocation expressions

A typed macro may appear in expression position:

```cx
macro Answer() -> int {
    return 42;
}

let answer: int = use Answer();
```

An `elements<T>` invocation has specialized behavior inside positional
initializers. See [Typed expression and element-sequence macros](../features/initializers-and-typed-macros.md#typed-expression-macros).

## Precedence

From tighter to looser binding, the implemented binary precedence groups are:

| Group | Operators |
| --- | --- |
| Multiplicative | `*`, `/`, `%` |
| Additive | `+`, `-` |
| Shift | `<<`, `>>` |
| Relational/comparison | `<`, `<=`, `>`, `>=`, `<=>` |
| Equality | `==`, `!=` |
| Bitwise AND | `&` |
| Bitwise XOR | `^` |
| Bitwise OR | `|` |
| Logical AND | `&&` |
| Logical OR | `||` |
| Assignment | `=`, `+=`, `-=`, `*=`, `/=`, `%=` |

Assignment operators associate right-to-left. Parentheses can always make the
intended grouping explicit.

## Related guides

- [Data types](02-data-types.md)
- [Variables and constants](03-variables-and-constants.md)
- [Initializers and typed AST macros](../features/initializers-and-typed-macros.md)
