# Arrays, slices, and iteration

CX separates storage from views and traversal. Fixed arrays own compile-time
sized inline storage, `Slice<T>` views a pointer plus length, `Range<T>` views a
pair of pointers, `Vec<T>` manages dynamic storage, and structural iterator
requirements let user types define custom traversal.

The complete example for this chapter is
[`examples/arrays-slices-and-iteration.cx`](../../examples/arrays-slices-and-iteration.cx).

## Fixed arrays

A fixed array includes its element type and length:

```cx
let values: int[4] = { 10, 20, 30, 40 };
```

The length is part of the resolved type. Storage is embedded directly in the
local, global, or containing struct:

```cx
struct MatrixRow {
    values: float[4];
}
```

Fixed arrays are not dynamic collections. Their capacity cannot change, and
they do not carry a hidden allocation or runtime length field.

## Inferred fixed-array lengths

Write `T[]` on a declaration to infer a fixed length from a non-empty
positional initializer:

```cx
const primes: int[] = { 2, 3, 5, 7 };
let offsets: usize[] = { 0, 8, 16 };
```

These resolve to `int[4]` and `usize[3]`. Later compiler phases see ordinary
fixed arrays with concrete lengths.

Inference requires a positional initializer with at least one element:

```cx
let empty: int[] = {};       // error
fn consume(values: int[]) {} // error: no initializer determines a length
```

CX does not infer portable zero-length arrays. Array inference also composes
with typed `elements<T>` macros after their initializer elements have been
expanded.

See the completed
[inferred fixed-array length design](../ideas/inferred-fixed-array-lengths.md)
and the [typed macro guide](../features/initializers-and-typed-macros.md).

## Indexing and size

Index with a zero-based integral expression:

```cx
let first = values[0];
values[1] = 42;
```

As in C, indexing does not introduce automatic runtime bounds checks.

Use `sizeof` when the byte size is needed:

```cx
let bytes: usize = sizeof(values);
let count: usize = sizeof(values) / sizeof(int);
```

Because a fixed array's length is semantic type information, `sizeof(values)`
includes the entire inline array rather than only a pointer.

## Iterating fixed arrays

`foreach` knows the fixed length and lowers to an indexed loop:

```cx
foreach index, value in values {
    consume(index, value);
}
```

The optional index is `usize`; the value type is the array element type. Types
may be written explicitly, but inference is normally sufficient:

```cx
foreach index: usize, value: int in values {
    consume(index, value);
}
```

An ordinary value binding is a copy. Assigning it does not update the array:

```cx
foreach value in values {
    value += 1; // changes the loop-local copy
}
```

Request a pointer binding to mutate the stored element:

```cx
foreach &value in values {
    *value += 1;
}
```

Use `const` when the loop-local binding must not be reassigned:

```cx
foreach const value in values {
    inspect(value);
}
```

The iteration index is immutable even when its modifier is omitted.

## Slices

`Slice<T>` is the standard non-owning contiguous view:

```cx
let slice: Slice<int> = {
    data: &values[0],
    length: 4
};
```

Its representation is deliberately transparent:

```cx
struct Slice<T>: Contiguous<T> {
    data: T*;
    length: usize;
}
```

A slice does not allocate, copy, resize, or free its elements. The referenced
storage must remain alive for every use of the slice.

Iteration caches `data` and `length` once, then uses an indexed loop:

```cx
foreach index, value in slice {
    consume(index, value);
}
```

Any user-defined type satisfying `Contiguous<T>` receives the same lowering.

## Pointer ranges

`Range<T>` represents a half-open contiguous pointer interval:

```cx
let middle: Range<int> = {
    start: &values[1],
    end: &values[3]
};
```

The first pointer is included and the end pointer is excluded. The iteration
length is calculated as `end - start`:

```cx
struct Range<T>: ContiguousRange<T> {
    start: T*;
    end: T*;
}
```

Like a slice, a pointer range is non-owning and requires valid related pointers
into live storage.

## Dynamic vectors

`Vec<T>` owns resizable heap storage while exposing the same `data` and
`length` shape used by contiguous iteration:

```cx
let values = Vec<int>.create();
values.add(10);
values.add(20);

foreach value in values {
    consume(value);
}

values.dispose();
```

Unlike a fixed array or slice, a vector has capacity and allocation ownership.
Dispose it explicitly or use a `using` binding where ownership should end
automatically with the scope.

Do not retain element pointers across operations that may reallocate the
vector.

## Scalar ranges

Scalar range expressions generate numeric or character sequences:

```cx
foreach value in 0..3 {
    // 0, 1, 2
}

foreach value in 0...3 {
    // 0, 1, 2, 3
}

foreach ch in 'a'...'z' {
    // inclusive alphabet
}
```

`..` excludes the end and `...` includes it. The end expression is evaluated
once. Scalar ranges advance forward by one; use an explicit `for` loop for a
different step or descending traversal.

## Custom value iterators

A type can provide the standard iterator requirements:

```cx
requires Iterable<T, I>
where I: Iterator<T> {
    fn iterator() -> I;
}

requires Iterator<T> {
    fn next() -> bool;
    fn value() -> T*;
}
```

The implicit requirement receivers are `Self*`. An iterable creates the
iterator by value. CX then repeatedly calls `next()` and obtains the current
element pointer from `value()`:

```cx
struct IntBag: Iterable<int, IntIterator> {
    data: int*;
    length: usize;
}

fn IntBag.iterator() -> IntIterator {
    return IntIterator {
        data: self.data,
        length: self.length,
        index: 0
    };
}
```

Conceptually, `foreach item in bag` lowers to:

```cx
let iterator = bag.iterator();
while (iterator.next()) {
    let item = *iterator.value();
    // body
}
```

An index binding adds a separate counter; the iterator does not need to expose
its own index.

The repository contains a complete executable
[`foreach-iterator.cx`](../../examples/foreach-iterator.cx) example.

## Key/value iterators

Mappings use parallel key and value bindings:

```cx
foreach key => value in map {
    consume(key, value);
}
```

An optional traversal index precedes them:

```cx
foreach index, key => value in map {
    consume(index, key, value);
}
```

The structural protocols are:

```cx
requires KeyValueIterable<K, V, I>
where I: KeyValueIterator<K, V> {
    fn iterator() -> I;
}

requires KeyValueIterator<K, V> {
    fn next() -> bool;
    fn key() -> K*;
    fn value() -> V*;
}
```

The complete
[`foreach-key-value-iterator.cx`](../../examples/foreach-key-value-iterator.cx)
example shows a custom implementation and mutable value traversal.

## Protocol selection priority

CX selects one lowering based on the resolved iterable type:

1. scalar range;
2. data enum;
3. iterator or key/value iterator protocol;
4. fixed array or contiguous storage protocol.

This ordering matters when a type has both `data`/`length` fields and an
`Iterable` implementation. Its iterator behavior wins, allowing a collection
to expose a logical traversal that differs from physical storage order.

See [`foreach-iterator-priority.cx`](../../examples/foreach-iterator-priority.cx)
for an executable demonstration.

## Lowering and evaluation guarantees

Foreach lowering creates typed AST rather than source fragments. Iterable
expressions and their traversal state are evaluated and cached as required,
and generated local names are deterministic and collision-safe.

The C backend never needs a generic `foreach` construct. It receives ordinary
`for` or `while` nodes with typed index, pointer, length, iterator, key, and
value operations.

## Current boundaries

- Fixed-array and contiguous indexing does not add runtime bounds checks.
- `T[]` inference requires a non-empty positional initializer.
- `Slice<T>` and `Range<T>` are non-owning views with no lifetime tracking.
- Scalar ranges use a forward step of one.
- Reference bindings require a protocol that can expose stable element
  pointers.
- Key/value syntax requires the key/value iterator protocol; `data` and
  `length` alone are not sufficient.
- Iterator implementations must keep pointers returned by `value()` or `key()`
  valid for the duration in which the loop uses them.

## Related chapters

- [Data types](02-data-types.md)
- [Variables and constants](03-variables-and-constants.md)
- [Control flow](07-control-flow.md)
- [Structs](05-structs.md)
- [Enums and data enums](08-enums-and-data-enums.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
