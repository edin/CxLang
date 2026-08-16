# Generics and requirements

CX generics describe reusable typed declarations that are specialized before C
emission. Requirements constrain those declarations structurally: they state
which fields, methods, static functions, or operators a concrete type must
provide, without introducing runtime interfaces or hidden dispatch.

The complete example for this chapter is
[`examples/generics-and-requirements.cx`](../../examples/generics-and-requirements.cx).

## Generic functions

Declare type parameters after a function name:

```cx
fn identity<T>(value: T) -> T {
    return value;
}
```

Calls may infer the type argument from ordinary arguments:

```cx
let number = identity(42);       // identity<int>
let text = identity("hello");    // identity<char*>
```

Or provide it explicitly:

```cx
let number = identity<int>(42);
```

Inference binds one consistent concrete type to each type parameter. It does
not erase argument types or treat generic values as dynamically typed.

## Generic structs

Struct type parameters describe fields and owned behavior:

```cx
struct Box<T> {
    value: T;
}

let integer: Box<int> = Box<int> { value: 42 };
let decimal: Box<double> = Box<double> { value: 3.5 };
```

`Box<int>` and `Box<double>` are distinct semantic types. Their concrete fields
and methods are resolved after substituting `T`.

Generic enums, adapters, interfaces, extensions, and function signatures use
the same structured type arguments where supported by their declarations.

## Specialization before C

The C backend does not receive unresolved CX generics. Each reachable concrete
use is specialized into ordinary declarations and functions:

```text
Box<int>      -> Box_int
Box<double>   -> Box_double
identity<int> -> identity_int
```

The precise generated spelling is a backend detail, but it is deterministic
and module-safe. Specialization preserves canonical function identity, resolved
calls, nested type arguments, and required dependencies.

Unused specializations are not emitted merely because a generic declaration
exists.

## Declaring a requirement

A requirement is a structural contract:

```cx
requires Resettable {
    fn reset() -> void;
}
```

The non-static method receives an implicit `self: Self*` receiver. The explicit
long form is equivalent:

```cx
requires Resettable {
    fn reset(self: Self*) -> void;
}
```

`Self` means the concrete type being tested against the requirement.

Requirements may themselves have type parameters:

```cx
requires Pushable<T> {
    fn push(value: T) -> bool;
}
```

## Field requirements

A requirement can describe representation needed by a generic algorithm or
compiler protocol:

```cx
requires Contiguous<T> {
    data: T*;
    length: usize;
}
```

A candidate satisfies it only when both fields exist with compatible resolved
types. For `Vec<int>`, the expected fields become `int*` and `usize`.

Aliases and specialized generic types are resolved before matching, so an
alias of `Vec<int>` is checked against the concrete substituted fields rather
than its source spelling.

## Instance method requirements

Method requirements include parameter and result types:

```cx
requires Pushable<T> {
    fn push(value: T) -> bool;
}
```

The method may be owned by the type or supplied by an applicable extension:

```cx
extension Vec<T> {
    fn push(value: T) -> bool {
        // ...
        return true;
    }
}
```

Matching checks the complete resolved signature. A `push(T) -> int` method does
not satisfy `push(T) -> bool` merely because the source name matches.

## Static function requirements

Use `static` when behavior belongs to the type rather than an instance:

```cx
requires Hash<T> {
    static fn hash(value: T) -> u64;
}
```

Static behavior can also be provided for compiler-known types through a
qualified function:

```cx
static fn int.hash(value: int) -> u64 {
    return (u64)value;
}
```

This lets generic algorithms use uniform static capabilities without wrapping
primitive values in artificial source structs.

## Operator requirements

Operators are ordinary typed capabilities in requirements:

```cx
requires Add<T> {
    fn operator +(other: T) -> T;
}

requires Compare<T> {
    fn operator <=>(other: T) -> int;
}
```

User-defined operator methods, derived comparisons, and compiler-known
primitive or enum operators can satisfy these requirements. Matching uses
typed operator identity and the resolved operand/result types, not the method's
generated name.

## Where clauses

Attach constraints after a generic declaration:

```cx
fn combine<T>(left: T, right: T) -> T
where T: Add<T> {
    return left + right;
}
```

The constraint performs two jobs:

1. it rejects concrete calls whose type does not provide the requirement;
2. it makes the requirement's operations valid inside the generic body.

Without `where T: Add<T>`, the compiler has no semantic basis for accepting
`left + right` for an arbitrary `T`.

## Multiple constraints

Join several requirements with `+`:

```cx
fn ordered_sum<T>(left: T, right: T) -> T
where T: Add<T> + Compare<T> {
    if ((left <=> right) > 0) {
        return right + left;
    }

    return left + right;
}
```

All listed requirements must match the same concrete `T`. Constraints may also
appear on generic structs and extensions:

```cx
struct SortedPair<T>
where T: Compare<T> {
    first: T;
    second: T;
}
```

## Requirements depending on requirements

A requirement may constrain one of its own parameters:

```cx
requires Iterable<T, I>
where I: Iterator<T> {
    fn iterator() -> I;
}
```

Matching `Iterable<int, IntIterator>` therefore also proves that
`IntIterator` satisfies `Iterator<int>`. This composes small structural facts
into higher-level protocols without runtime inheritance.

## Declaring provided requirements

A struct can state the requirements it intends to provide:

```cx
struct IntView: Contiguous<int> {
    data: int*;
    length: usize;
}
```

The colon is a checked capability declaration, not storage or class
inheritance. CX validates the requirement name, generic arity, dependent
constraints, and required members. For example, omitting `length` produces a
specific failure for the missing field.

A declaration can provide several requirements by separating them with commas:

```cx
struct Resource: Resettable, Disposable<Resource> {
    // ...
}
```

Requirements remain structurally matchable by the shared matcher; explicit
declarations make the intended public capabilities clear and allow the
declaration itself to be validated.

## Constrained extensions

Extensions can exist only for receiver specializations satisfying a
requirement:

```cx
extension Box<T>
where T: Add<T> {
    fn add(other: T) -> T {
        return self.value + other;
    }
}
```

`Box<int>` receives this method because intrinsic integer addition satisfies
`Add<int>`. A `Box<Plain>` without addition does not.

Constrained extensions participate in normal method lookup and overload
resolution. Inapplicable candidates are removed before ranking; they do not
create false ambiguities.

## Constrained overloads

Several methods may share a source name while only some are applicable to a
receiver's concrete type. The function catalog keeps the complete overload set
and evaluates each candidate's constraints before normal arity and conversion
ranking.

If no constrained candidate applies, the call is rejected. If multiple
applicable candidates rank equally, CX reports an ambiguous call with the
candidate signatures rather than selecting by declaration order.

## Self in requirements

`Self` allows a requirement to describe recursive relationships:

```cx
requires Cloneable {
    fn clone() -> Self;
}

requires Linkable {
    next: Self*;
}
```

When checked against `Node`, both occurrences become `Node`; when checked
against `Box<int>`, they become `Box<int>`. `Self` is a semantic binding, not a
string substitution.

## Diagnostics

Requirement failures describe the structural mismatch. Examples include:

- an unknown or ambiguous requirement declaration;
- the wrong number of requirement type arguments;
- a missing field, method, static function, or operator;
- an existing field with the wrong resolved type;
- a method with incompatible parameters or return type;
- an unsatisfied dependent `where` constraint;
- a constrained method or extension unavailable for the receiver type.

Diagnostics use the concrete substituted types. For example, a field mismatch
reports that `data` is `double*` when `int*` was expected, rather than exposing
an internal failure to bind `T`.

## Compile-time requirement matching

Compile-time code can inspect whether a type satisfies a requirement and read
the resulting type bindings. This makes the same semantic matcher available to
typed macros that generate behavior conditionally:

```cx
@let match = target.match(Debug);
@if(match.success) {
    // generate Debug-based behavior
}
```

The returned match describes bindings such as `Self`, `T`, or iterator types,
so macros do not need to reproduce structural matching with name-based tests.

## Current boundaries

- Generics are specialized at compile time; CX does not emit erased runtime
  generic values.
- Requirements are structural contracts, not runtime interface values.
- A requirement name must resolve uniquely in the relevant module context.
- Generic inference needs enough argument or expected-type information to bind
  every required type parameter.
- Constraint satisfaction uses exact resolved structure and signatures; names
  alone are insufficient.
- Recursive or mutually dependent capabilities must still produce a finite set
  of concrete specializations.

## Related chapters

- [Structs](05-structs.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Arrays, slices, and iteration](10-arrays-slices-and-iteration.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
