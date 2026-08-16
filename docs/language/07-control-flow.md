# Control flow

CX keeps familiar C control flow while adding typed iteration, exhaustive
matching, and structured `Result` propagation. High-level constructs lower to
ordinary branches, loops, switches, and temporary values in generated C.

The complete example for this chapter is
[`examples/control-flow-guide.cx`](../../examples/control-flow-guide.cx).

## Conditional statements

Use `if`, `else if`, and `else` for conditional execution:

```cx
fn classify(value: int) -> int {
    if (value < 0) {
        return -1;
    } else if (value == 0) {
        return 0;
    } else {
        return 1;
    }
}
```

Conditions must be compatible with `bool`. Each block introduces its own
lexical scope.

For a conditional value rather than conditional statements, use the ternary
expression:

```cx
let maximum = left > right ? left : right;
```

Both branches must produce compatible types. See
[Expressions and operators](04-expressions-and-operators.md#conditional-expressions).

## While loops

A `while` loop evaluates its condition before every iteration:

```cx
while (remaining > 0) {
    remaining -= 1;
}
```

Because the condition is checked first, the body may execute zero times.

## For loops

The C-style `for` form keeps initialization, condition, and increment together:

```cx
for (let i: int = 0; i < count; i += 1) {
    total += values[i];
}
```

The initializer binding is scoped to the loop. CX supports assignment and
increment expressions in the final clause:

```cx
for (let i = 0; i < count; i++) {
    // ...
}
```

## Break and continue

`break` exits the nearest loop or switch. `continue` skips to the next
iteration of the nearest loop:

```cx
for (let i = 0; i < count; i += 1) {
    if (should_skip(i)) {
        continue;
    }

    if (finished(i)) {
        break;
    }
}
```

When a scope owns `using` resources, CX inserts required cleanup before a
`break`, `continue`, or early `return`. Control transfer does not bypass
deterministic cleanup.

## Foreach loops

`foreach` binds values from an iterable without exposing its lowering protocol:

```cx
foreach item in values {
    total += item;
}
```

An optional first binding receives the zero-based iteration index:

```cx
foreach index, item in values {
    visit(index, item);
}
```

Binding types are inferred when omitted, or may be stated explicitly:

```cx
foreach index: usize, item: int in values {
    visit(index, item);
}
```

Arrays and contiguous types lower to indexed loops. Iterator-backed types use
their `iterator`, `next`, and `value` behavior. Key/value iterators use `=>`:

```cx
foreach index, key => value in map {
    consume(index, key, value);
}
```

Bindings may also request const or reference behavior where the iterable
supports it:

```cx
foreach const item in values {
    inspect(item);
}

foreach &item in values {
    update(item);
}
```

The full iterable protocols, mutability rules, and lowering priorities will be
covered in the arrays, slices, and iteration chapter.

## Scalar ranges

Two range operators distinguish exclusive and inclusive endpoints:

```cx
foreach value in 0..10 {
    // 0 through 9
}

foreach value in 0...10 {
    // 0 through 10
}
```

Scalar ranges lower directly to `for` loops. The end expression is evaluated
once and cached, rather than being reevaluated on every iteration. When an
index binding is requested, CX maintains a separate zero-based counter:

```cx
foreach index, value in start..end {
    consume(index, value);
}
```

Range value types and the index type are inferred; the index defaults to
`usize`.

## Switch statements

`switch` provides C-style selection over integral and enum values:

```cx
switch (status) {
    case Status.Ready: {
        start();
        break;
    }
    case Status.Stopped: {
        reset();
        break;
    }
    default: {
        report_unknown();
        break;
    }
}
```

Cases use explicit `break` when execution should leave the switch. Enum case
labels should be qualified with their owning type so generated C names remain
unambiguous.

Use `switch` when several scalar values choose behavior. Use `match` when a
tagged-union arm must also bind a typed payload.

## Return flow

`return` ends the current function immediately:

```cx
fn absolute(value: int) -> int {
    if (value < 0) {
        return -value;
    }

    return value;
}
```

Value-returning functions must return on every reachable path. CX understands
branch structure and exhaustive tagged-union matches when checking return
flow. It also reports unreachable statements after control flow has already
terminated.

## Propagating results with try

CX's prefix `try` unwraps a successful `Result<T, Error>` or returns the error
from the containing function:

```cx
fn read_value(success: bool) -> Result<int, Error> {
    if (success) {
        return Result.ok<int, Error>(41);
    }

    return Result.err<int, Error>(
        Error.create("example", 1, "read failed"));
}

fn increment(success: bool) -> Result<int, Error> {
    let value: int = try read_value(success);
    return Result.ok<int, Error>(value + 1);
}
```

Without a fallback, the containing function must return a compatible
`Result<T, Error>`. On failure, CX constructs the propagated error result,
performs any pending scoped cleanup, and returns it.

This is structured control flow, not an exception mechanism: the generated C
tests the result and uses an ordinary early return.

## Try fallbacks

Combine `try` with `??` to consume an error and produce a fallback value:

```cx
fn value_or_default(success: bool) -> int {
    return try read_value(success) ?? 7;
}
```

Because the error is handled, the containing function does not need to return
`Result`. The fallback is lazy—it runs only when the attempted result is an
error.

Fallbacks can be chained:

```cx
let value = try read_from_cache()
    ?? try read_from_disk()
    ?? try read_from_network()
    ?? default_value();
```

CX evaluates the chain from left to right and stops at the first success. The
final expression supplies the plain value if every attempted result fails.

## Match control flow

Tagged-union `match` is exhaustive and binds a payload specific to each arm:

```cx
match result {
    Ok: value => return value;
    Error: error => return recover(error);
}
```

It lowers to a switch over the stored tag. See
[Tagged unions and matching](09-tagged-unions-and-matching.md) for bindings,
wildcards, exhaustiveness diagnostics, pointer matching, and generated C.

## Runtime and compile-time control flow

The constructs in this chapter execute in the generated program. Compile-time
directives are visibly prefixed with `@`:

```cx
@if(target.is_struct) {
    // expanded by the compiler
}

@foreach field in target.fields {
    // expanded by the compiler
}
```

Keeping the forms distinct makes it clear whether a branch or loop survives
into generated C. See the
[initializer and typed macro guide](../features/initializers-and-typed-macros.md)
for compile-time control flow.

## Current boundaries

- `switch` retains explicit C-style `break` behavior.
- Scalar ranges currently advance forward by one; descending or custom-step
  ranges should use an explicit loop.
- A propagating `try` requires a compatible `Result<T, Error>` return type.
- `??` in this model handles `try` fallbacks; it does not introduce exception
  handling.
- Iterable reference and key/value bindings depend on capabilities supplied by
  the selected iteration protocol.

## Related chapters

- [Expressions and operators](04-expressions-and-operators.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Enums and data enums](08-enums-and-data-enums.md)
- [Tagged unions and matching](09-tagged-unions-and-matching.md)
- [Variables and constants](03-variables-and-constants.md)
