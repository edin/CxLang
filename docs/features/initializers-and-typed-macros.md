# Initializers and typed AST macros

CX initializers begin as a convenient way to construct ordinary C-shaped
values. The same structured AST now supports inferred fixed arrays and typed
compile-time generation without falling back to text substitution.

This guide builds the feature up one layer at a time and ends with the PHP
extension experiment that generates real Zend argument metadata from reflected
CX functions.

## Constructor calls

The simplest construction syntax is a positional call using the struct name:

```cx
struct Point {
    x: int;
    y: int;
}

let point: Point = Point(2, 3);
```

The arguments follow field declaration order. The semantic model records this
as a struct-constructor call rather than resolving it as an ordinary function.

Tagged-union variants use qualified constructor calls:

```cx
union Value {
    Number: int;
    Position: Point;
}

let value: Value = Value.Position(100, 200);
```

Constructor calls are convenient for compact positional construction.
Initializer expressions provide named fields, contextual typing, nesting, and
the compile-time generation features described below.

## Struct construction

A typed initializer names the value being constructed and may initialize its
fields by name:

```cx
struct Point {
    x: int;
    y: int;
}

let origin = Point {
    x: 0,
    y: 0
};
```

Initializers are expressions, so field values may be arbitrary compatible
expressions:

```cx
let base: int = 10;
let point = Point {
    x: base + 1,
    y: base > 5 ? 20 : 30
};
```

They may also be nested:

```cx
struct Pixel {
    position: Point;
    color: Color;
}

let pixel = Pixel {
    position: Point { x: 12, y: 34 },
    color: Color { r: 0.25, g: 0.5, b: 1.0 }
};
```

When the surrounding declaration already supplies the type, the initializer
may omit it:

```cx
let second: Point = {
    x: point.x + 1,
    y: point.y + 1
};
```

CX retains a structured initializer node through semantic analysis and lowers
it to an ordinary C initializer or compound literal. It does not turn the
initializer into source text and parse it again.

## Positional initializers

Positional initializers are useful for arrays and C-shaped records:

```cx
let values: int[3] = { 10, 20, 30 };

let pair: Pair = { 20, 22 };
```

Nested positional initializers remain structured:

```cx
const pairs: Pair[2] = {
    { 10, 20 },
    { 30, 40 }
};
```

The expected element or aggregate type is propagated into nested initializers
before C lowering.

## Inferred fixed-array lengths

`T[]` on an initialized declaration asks CX to infer a fixed array length:

```cx
const values: int[] = { 10, 20, 30 };
```

The resolved type is `int[3]`. This is not a dynamic array or slice. Generated
C contains the concrete size:

```c
const int values[3] = { 10, 20, 30 };
```

Inference works for global and local declarations. The initializer must be
positional and non-empty because portable C has no general zero-length fixed
array:

```cx
let empty: int[] = {}; // diagnostic
```

Once resolved, the inferred length becomes part of the ordinary fixed-array
semantic type used by indexing, `sizeof`, `foreach`, reflection, and lowering.

## Existing statement and declaration macros

CX macros already had structural contracts for generating statements and
declarations:

```cx
macro Trace(value: expression) -> statements {
    printf("value=%d\n", @{value});
}

macro Wrap(function: declaration) -> declarations {
    fn @{as_name(concat("wrap_", function.name))}(
        @{function.parameters}
    ) -> @{function.return_type} {
        return @{as_name(function.name)}(@{function.parameters});
    }
}
```

The parameter kinds describe the compile-time input expected by the macro.
Current macro parameters include structured inputs such as `expression`,
`name`, `type`, `declaration`, and `module`.

`use` expands the macro only in a position compatible with its result
contract. A statement macro cannot be used as an expression, for example.

## Typed expression macros

A macro may now declare an ordinary CX result type:

```cx
macro Answer() -> int {
    return 42;
}

const answer = use Answer();
```

`use Answer()` is an expression AST node. Expansion replaces it with the
returned expression, and normal inference resolves `answer` as `int`.

Typed aggregate results work as well:

```cx
macro PairValue() -> Pair {
    return { 20, 22 };
}

let pair: Pair = use PairValue();
```

The declared result type gives the returned untyped initializer its aggregate
type. CX then validates the expanded expression against that contract.

A typed expression macro must expand to exactly one `return` statement with a
value. Compile-time statements may select which return remains:

```cx
macro Choose() -> int {
    @if (true) {
        return 42;
    }

    @if (false) {
        return 0;
    }
}
```

## Element-sequence macros

`elements<T>` is a distinct structural result contract:

```cx
macro Values() -> elements<int> {
    return { 10, 20, 30 };
}
```

It means that the macro returns zero or more initializer elements of type `T`,
not one array-valued expression.

The sequence can provide a complete initializer:

```cx
const values: int[] = use Values();
```

Because the macro result declares its element type and produces three values,
CX infers `values` as `int[3]` even when the declaration omits its type:

```cx
const values = use Values();
```

The same sequence can be spliced into a containing positional initializer:

```cx
const extended: int[] = {
    0,
    use Values(),
    40
};
```

Expansion produces the equivalent of:

```cx
const extended: int[5] = { 0, 10, 20, 30, 40 };
```

Flattening is not guessed from the surrounding braces. It follows explicitly
from the macro's `elements<int>` result contract. A macro returning an array
type such as `int[3]` represents one array value and is not automatically
spliced.

Every generated element is checked against `T`. Aggregate elements receive
the declared type before validation:

```cx
macro Pairs() -> elements<Pair> {
    return {
        { 10, 20 },
        { 30, 40 }
    };
}
```

An empty sequence is valid when spliced into an existing initializer. It
cannot be the complete initializer of an inferred array because no portable
fixed length can be inferred.

## Compile-time directives inside initializers

Positional initializers may contain compile-time `@if` and `@foreach`
directives. Each directive expands to zero or more elements at its exact
position:

```cx
compile fn generated_values() -> list<int> {
    return [10, 20, 30];
}

macro GeneratedValues() -> elements<int> {
    return {
        0,

        @foreach value in generated_values() {
            @{value},
        }

        @if (true) {
            40,
        }
    };
}
```

This returns `{ 0, 10, 20, 30, 40 }`. Directives may be nested, `@if` may
have an `else` branch, and an empty loop naturally produces no elements.

Initializer directives currently generate positional elements. Compile-time
setup such as `@let` belongs in the macro body before `return`; `@if` and
`@foreach` inside the returned initializer perform the element generation.

## Reflection-driven generation

A macro parameter of kind `declaration` can receive a function declaration and
inspect its compile-time reflection data:

```cx
macro ParameterNames(
    function: declaration
) -> elements<const char*> {
    return {
        @foreach parameter in function.parameters {
            @{parameter.name},
        }
    };
}
```

The same pattern can inspect parameter types, attributes, default metadata,
and the function return type. Placeholders inject structured compile-time
results back into the returned AST.

## Real example: PHP argument information

The PHP extension experiment uses these features to generate Zend argument
metadata from ordinary functions marked with `@php_export`.

The reusable macro has a typed element contract. In abbreviated form, its
structure is:

```cx
macro PhpArgInfos(
    function: declaration
) -> elements<ZendInternalArgInfo> {
    @let optional_parameters = [];
    @foreach parameter in function.parameters {
        @if (parameter.attribute("php_optional") != null) {
            optional_parameters.add(parameter);
        }
    }

    return {
        {
            (const char*)@{
                function.parameters.count - optional_parameters.count
            },
            { null, (u32)@{php_export_type_mask(function.return_type)} },
            null
        },

        @foreach parameter in function.parameters {
            @if (parameter.attribute("php_optional") == null) {
                {
                    @{parameter.name},
                    { null, (u32)@{php_export_type_mask(parameter.type)} },
                    null
                },
            }

            // The optional branch emits the same shape with reflected
            // default-value display text in the final field.
        }
    };
}
```

The actual binding keeps one flat array for the complete PHP module:

```cx
const cx_generated_arg_info: ZendInternalArgInfo[] = {
    @foreach function in exports {
        use PhpArgInfos(@{function.reference}),
    }
};
```

For the current eight exported functions, CX emits an ordinary C declaration
with the inferred final size:

```c
const ZendInternalArgInfo cx_generated_arg_info[21] = {
    /* reflected return headers and parameter entries */
};
```

The generated metadata includes:

- the required argument count in each Zend header entry;
- the PHP type mask for every return and parameter type;
- reflected parameter names;
- optional parameter default text;
- stable offsets used by the generated function table.

The previous implementation allocated the same array and populated every
field at runtime through setter calls. The typed initializer version is static,
const, exact-sized data, and PHP's reflection and runtime tests consume it
successfully.

See the complete implementation in
[`php_binding.cx`](../../experiments/php-extension/src/php_binding.cx).

## Semantic guarantees

These features remain AST-based through the compiler pipeline:

- Macro result contracts distinguish one expression from an element sequence.
- Macro expansion occurs before ordinary type inference and semantic analysis.
- Returned expressions and every returned element are validated against the
  declared result type.
- Inferred array lengths are resolved only after expansion determines the
  final element count.
- Nested aggregate initializers receive structured type information.
- Compile-time-only invocation and directive nodes are diagnosed if they ever
  survive toward C lowering.
- Generated source locations and macro origins are retained for diagnostics.

There is no C or CX source-string generation in this path. Macros return AST,
the compiler expands AST into AST, and the normal semantic pipeline verifies
the result.

## Result contracts at a glance

| Macro result | Meaning | Valid use |
| --- | --- | --- |
| `statements` | Zero or more statements | Statement position |
| `declarations` | Zero or more declarations | Declaration position |
| `T` | Exactly one expression assignable to `T` | Expression position |
| `elements<T>` | Zero or more positional initializer elements of type `T` | Complete or containing positional initializer |

## Verification

The feature family is covered by the compiler tests for expression parsing,
AST completeness and rewriting, inferred fixed arrays, macro result typing,
initializer splicing, nested directives, empty expansion, and diagnostics.

The PHP extension additionally verifies the generated shared library through
PHP smoke tests, reflection checks, and a 100,000-call stress test with no
observed memory growth.
