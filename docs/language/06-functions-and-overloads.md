# Functions and overloads

Functions are the center of CX's executable model. Free functions, methods,
extensions, operators, extern declarations, generic specializations, and
function values all participate in one typed function system and lower to
readable C functions or function pointers.

The complete example for this chapter is
[`examples/functions-and-overloads.cx`](../../examples/functions-and-overloads.cx).

## Declaring and calling functions

A function declaration names every parameter and states its return type:

```cx
fn add(left: int, right: int) -> int {
    return left + right;
}

let answer: int = add(20, 22);
```

Use `void` when no value is returned:

```cx
fn reset(counter: Counter*) -> void {
    counter.value = 0;
}
```

Calls are checked for argument count and type compatibility before C lowering.
Return statements are checked against the declared result type, and CX reports
missing return paths for value-returning functions.

Parameter names are part of the declaration and local scope, but calls are
positional.

## Overloads

Several CX functions may share a source name when their signatures differ:

```cx
fn choose(value: int) -> int {
    return 1;
}

fn choose(value: char) -> int {
    return 2;
}

let integer_choice = choose(10);
let character_choice = choose('x');
```

Overload resolution considers the complete candidate set. It uses arity,
receiver type, generic constraints, and argument compatibility, preferring an
exact type match over a conversion.

CX does not select the first declaration with a matching name. If two
candidates are equally good, compilation fails and the ambiguity diagnostic
lists their signatures:

```cx
fn convert(value: char) -> int { return 1; }
fn convert(value: long) -> int { return 2; }

let result = convert(10); // error: both conversions rank equally
```

Because C has no source-level overloads, reachable overloads receive stable,
distinct generated names such as `Value_create_int` and
`Value_create_char_ptr`.

## Generic functions

Type parameters follow the function name:

```cx
fn identity<T>(value: T) -> T {
    return value;
}
```

CX can infer type arguments from call arguments:

```cx
let answer: int = identity(42);
```

They may also be written explicitly:

```cx
let answer: int = identity<int>(42);
```

Generic functions are specialized for their concrete type arguments before C
emission. A generic definition therefore becomes ordinary typed C functions;
there is no runtime generic dispatch.

Generic constraints and their effect on overload eligibility are covered in
the planned generics and requirements chapter.

## Methods and static functions

Functions owned by a type use the same call-resolution system:

```cx
struct Counter {
    value: int;

    static fn create(value: int) -> Counter {
        return Counter { value: value };
    }

    fn add(amount: int) -> void {
        self.value = self.value + amount;
    }
}

let counter = Counter.create(10);
counter.add(5);
```

Static and instance methods may be overloaded by argument type. Instance
resolution also accounts for the receiver, including its generic arguments and
applicable constrained extensions.

See [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
for owned methods, qualified declarations, extensions, and implicit `self`.

## Function types

A function type describes the parameter and result types without naming a
particular function:

```cx
type BinaryOp = fn(int, int) -> int;

fn apply(operation: BinaryOp, left: int, right: int) -> int {
    return operation(left, right);
}
```

Parameter names may be included when they improve readability:

```cx
type Predicate = fn(value: int) -> bool;
```

They do not change function-type identity.

## Function values

A compatible function can be stored and called through a function value:

```cx
fn add(left: int, right: int) -> int {
    return left + right;
}

let operation: BinaryOp = add;
let answer = operation(20, 22);
```

Function values lower directly to C function pointers. CX ensures referenced
functions are declared before global function-pointer initializers and retains
functions that remain reachable only through such values.

## Function expressions

A function expression creates a non-capturing function value:

```cx
let compare: BinaryOp =
    fn(left: int, right: int) -> int => left <=> right;
```

When the expected function type supplies the result type, the arrow result may
be inferred:

```cx
fn positive_predicate() -> fn(int) -> bool {
    return fn(value: int) => value > 0;
}
```

Function expressions are hoisted to deterministically named C functions and
the expression becomes a pointer to the generated function. No closure
environment is emitted, so state needed by a callback should be passed
explicitly through its parameters or another data structure.

## Extern functions

Use `extern` to declare a function implemented outside CX:

```cx
extern fn puts(value: const char*) -> int;

fn greet() -> void {
    puts("Hello from CX");
}
```

The declaration supplies type information for CX and emits the ABI-facing C
declaration, but no function body.

An extern name represents one external ABI symbol and therefore cannot be
overloaded with different signatures:

```cx
extern fn convert(value: int) -> int;
extern fn convert(value: char*) -> int; // error: one ABI symbol
```

Repeating an identical extern declaration is allowed.

For declarations grouped with C types, headers, and macros, see the planned C
interop chapter and `declare c` blocks.

## Variadic functions

Extern and C declarations can expose C variadic signatures:

```cx
declare c {
    fn printf(format: const char*, ...) -> int;
}

printf("answer = %d\n", 42);
```

CX checks the fixed arguments against their declared parameter types. The
arguments represented by `...` follow the target C ABI and its promotion
rules; the format string and variadic values must agree just as they would in
C.

Function types can also represent variadic callbacks:

```cx
type Callback = fn(int, const char*, ...) -> double;
```

## Generated declarations and reachability

CX determines canonical function identity before lowering. This matters
because a function may be reached through:

- a direct call;
- an overload selected from several declarations;
- a specialized generic call;
- an instance, extension, or operator call;
- a function pointer;
- metadata such as a data-enum function value.

The C reachability pass retains all required dependencies while removing
unreachable CX functions when pruning is enabled. Generated names remain
deterministic and overload-safe.

## Current boundaries

- Function declarations state their return type explicitly.
- Calls are positional; CX does not currently provide named call arguments.
- Function expressions lower to ordinary function pointers rather than closure
  objects; they do not carry a captured environment.
- Extern functions cannot overload one ABI symbol with different signatures.
- C variadic arguments do not gain runtime format or type metadata.
- Generic functions are compile-time specializations, not runtime-polymorphic
  values.

## Related chapters

- [Data types](02-data-types.md)
- [Expressions and operators](04-expressions-and-operators.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Enums and data enums](08-enums-and-data-enums.md)
- [Tagged unions and matching](09-tagged-unions-and-matching.md)

Compile-time functions and macros use related declaration syntax but execute in
the compiler rather than the generated program. They are covered by the
[initializer and typed macro guide](../features/initializers-and-typed-macros.md).
