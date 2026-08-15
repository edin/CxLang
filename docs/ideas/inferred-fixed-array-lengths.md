---
status: implemented
area: language
created: 2026-08-15
completed: 2026-08-15
---

# Inferred fixed-array lengths

CX can infer the length of a fixed array from a non-empty positional
initializer. This removes duplicated size declarations while preserving a
fully static array type throughout the compiler pipeline.

## Motivation

Previously, a programmer had to write the array length twice:

```cx
const values: int[3] = { 10, 20, 30 };
```

The declared length and the number of initializer elements could drift apart
as the array evolved. This was particularly awkward for generated lookup
tables and foreign-function metadata, where compile-time code may eventually
produce the initializer elements.

CX now accepts an omitted length when the initializer provides enough
information:

```cx
const values: int[] = { 10, 20, 30 };

fn main() -> int {
    let offsets: int[] = { 4, 8 };
    return values[2] + offsets[0];
}
```

The declarations resolve to `int[3]` and `int[2]`. They are fixed arrays, not
dynamic arrays, slices, or pointers.

## Semantic rules

- `T[]` requests fixed-array length inference.
- The declaration must have a positional initializer.
- The initializer must contain at least one element.
- The number of positional elements becomes the array length.
- The inferred length becomes part of the resolved semantic type.
- Later compiler phases see an ordinary `T[N]`; they do not need special
  handling for inference.
- Explicit and symbolic lengths such as `T[4]` and `T[Capacity]` keep their
  existing meaning.

An inferred length cannot be used where no initializer determines its value,
such as a function parameter:

```cx
fn consume(values: int[]) -> int {
    return 0;
}
```

CX reports:

```text
Array length inference requires a positional initializer with at least one element.
```

An empty initializer receives the same diagnostic:

```cx
let values: int[] = {};
```

CX does not infer a zero-length array because standard C does not provide
portable zero-length fixed arrays.

## Generated C

The resolved size is explicit in generated C:

```c
const int values[3] = { 10, 20, 30 };

int main()
{
    int offsets[2] = { 4, 8 };
    return values[2] + offsets[0];
}
```

This keeps the output readable and gives the C compiler a conventional,
compile-time-sized declaration.

## Compiler model

The parser represents an omitted array length as an explicit inferred-length
syntax node rather than treating it as malformed syntax. During type
inference, a valid initializer resolves that node to an integer length. The
result is stored as the canonical fixed-array `TypeRef` used by indexing,
`sizeof`, `foreach`, reflection, lowering, and C emission.

If inference cannot resolve the length, semantic analysis reports a diagnostic
before C lowering. No unresolved inferred length is encoded as C text.

## Future applications

The feature is intentionally useful without macros, but it also establishes a
foundation for typed initializer-generation primitives. A future
initializer-producing macro could support declarations such as:

```cx
const optional_sum_arg_info: ZendInternalArgInfo[] =
    use ArgInfo(optional_sum);
```

After macro expansion supplies the initializer elements, the same array-length
inference can resolve the final fixed size. Initializer-producing macros remain
future work and are not part of this completed feature.

## Implementation status

Implemented and verified on 2026-08-15.

Coverage includes:

- parsing and formatting the inferred `T[]` syntax;
- global array inference;
- local array inference;
- emitted fixed-array sizes in C;
- diagnostics for empty initializers and contexts without an initializer.

The regression coverage lives in
[`InferredArrayLengthTests.cs`](../../tests/Cx.Compiler.Tests/InferredArrayLengthTests.cs)
and [`TypeNodeParsingTests.cs`](../../tests/Cx.Compiler.Tests/TypeNodeParsingTests.cs).

The complete repository verification gate passes with 880 compiler tests and
49 embedded standard-library tests.
