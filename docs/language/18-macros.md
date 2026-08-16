# Macros

CX macros are typed AST transformations. They receive compile-time values or
structured program objects and produce statements, declarations, one typed
expression, or initializer elements. Expansion inserts AST into AST; CX does
not generate source text and parse it again.

The compact executable example is
[`examples/macros-guide.cx`](../../examples/macros-guide.cx). The
[`macro-debug.cx`](../../examples/macro-debug.cx) example demonstrates
reflection, requirements, attributes, and generated extensions together.

## Declaring and invoking a macro

A macro declaration has parameters, a result contract, and a template body:

```cx
macro Trace(label: expression, value: expression) -> statements {
    printf("%s=%d\n", @{label}, @{value});
}
```

Invoke it with `use` in a compatible context:

```cx
use Trace("answer", answer);
```

The invocation does not become a runtime call. It is replaced by the generated
statement before normal semantic analysis and C lowering.

Macro declarations remain in the compiler AST as reusable templates, but are
not emitted to C.

## Parameter kinds

Macro parameters describe the kind of compile-time input expected by the
template:

| Parameter kind | Receives |
| --- | --- |
| `expression` | An expression AST supplied at the invocation site |
| `name` | An identifier-like compile-time name |
| `type` | A structured reflected type |
| `declaration` | A resolved source declaration, commonly a function |
| `module` | A reflected module |

For example, a declaration macro can inspect a function without reconstructing
its signature from text:

```cx
macro Wrap(function: declaration) -> declarations {
    fn @{as_name(concat("wrap_", function.name))}(
        @{function.parameters}
    ) -> @{function.return_type} {
        return @{function.reference}(@{function.parameters});
    }
}

use Wrap(add);
```

Arguments are validated against the declared parameter kind. Passing an
integer where a declaration or module is required produces a macro-invocation
diagnostic rather than failing later during generation.

## Result contracts

The result contract determines both what a macro generates and where `use` is
legal:

| Result | Meaning | Invocation context |
| --- | --- | --- |
| `statements` | Zero or more statements | Statement position |
| `declarations` | Zero or more declarations | Module or supported declaration position |
| `T` | Exactly one expression assignable to `T` | Expression position |
| `elements<T>` | Zero or more initializer elements of type `T` | Positional initializer |

This contract is how the compiler distinguishes a single array-valued
expression from a sequence that must be flattened into another initializer.
It does not guess from braces or the surrounding expected type.

## Statement macros

A statement macro expands directly into a function body:

```cx
macro Guard(value: expression) -> statements {
    if (!@{value}) {
        return -1;
    }
}

fn run(ok: bool) -> int {
    use Guard(ok);
    return 0;
}
```

The expanded statements are resolved in their destination scope. Expressions
passed to the macro retain their structured syntax and source origin.

A statement macro cannot be invoked where an expression or declaration is
required.

## Declaration macros

Declaration macros generate functions, structs, methods, extensions, and
other declarations:

```cx
macro GenerateDouble() -> declarations {
    fn generated_double(value: int) -> int {
        return value * 2;
    }
}

use GenerateDouble();
```

They can also be invoked inside supported type bodies. `Self` at such an
invocation refers to the containing type:

```cx
struct User {
    id: int;

    use Debug(Self);
}
```

The destination controls ownership and module placement of generated
declarations. Generated names still participate in ordinary collision,
visibility, overload, and semantic checks.

## Typed expression macros

An ordinary CX result type declares a single-expression macro:

```cx
macro Answer() -> int {
    return 42;
}

let answer = use Answer();
```

The macro returns expression AST, not the compile-time integer value itself.
After expansion, normal inference determines that `answer` is `int` and checks
the returned expression against the macro contract.

Aggregate results can use contextual initializers:

```cx
macro PairValue() -> Pair {
    return { 20, 22 };
}

let pair: Pair = use PairValue();
```

Compile-time directives may select which return survives. The final expansion
must contain exactly one returned expression compatible with the declared
type.

CX uses `-> T` directly rather than spelling this contract as
`expression<T>`: a typed macro result already means one expression of `T`.

## Initializer element macros

`elequence of initializer elements:ements<T>` explicitly means a s

```cx
macro Values() -> elements<int> {
    return { 10, 20, 30 };
}
```

It can provide an entire inferred array:

```cx
const values = use Values(); // int[3]
```

Or it can splice into a larger initializer:

```cx
const extended: int[] = {
    0,
    use Values(),
    40
};
```

The latter becomes a five-element array. Flattening occurs because `Values`
returns `elements<int>`. A macro returning `int[3]` would produce one array
expression and would not be flattened.

`@if` and `@foreach` inside the returned initializer can generate zero or more
elements at their exact location. Every final element is checked against `T`,
and inferred fixed-array length is computed after expansion.

The full initializer model, including nested aggregates and the PHP argument
metadata generator, is documented in
[Initializers and typed AST macros](../features/initializers-and-typed-macros.md).

## AST splicing

`@{expression}` evaluates compile-time data and inserts it into the surrounding
template slot:

```cx
@foreach field in target.fields {
    printf("%s\n", @{field.name});
}
```

The expected slot determines what may be spliced: a name, type, expression,
parameter sequence, declaration reference, initializer value, or other
supported syntax object. Examples include:

```cx
fn @{as_name(concat("get_", field.name))}() -> @{field.type} {
    return self.@{field.name};
}
```

`as_name` deliberately converts text into identifier data. Types remain
structured `type` values, and callable reflection objects expose `reference`;
macros need not format either as source strings.

## Directives inside templates

Macros use the same compile-time directives described in the
[compile-time programming chapter](17-compile-time-programming.md):

```cx
macro Inspect(target: type) -> statements {
    @let fields = target.fields;

    @foreach field in fields {
        @if(field.type == int) {
            consume(@{field.name});
        }
    }
}
```

- `@let` stores an evaluator value.
- `@if` selects syntax and supports a plain `else` branch.
- `@foreach` repeats syntax for every compile-time list item.
- `@{...}` splices a value into an AST slot.

Directives may be nested. Their lexical evaluator scope is distinct from the
runtime scopes represented by the generated template.

## Reflection-driven macros

Reflected types expose fields and methods. Functions expose parameters, return
types, signatures, visibility, attributes, and callable references. Modules
expose public functions and types.

This makes patterns such as serializers, dispatch tables, bindings, wrappers,
and debug implementations direct:

```cx
@foreach handler in target.public_functions {
    @let route = handler.attribute("route");

    @if(route != null) {
        register_route(
            @{route.method},
            @{route.path},
            @{handler.reference}
        );
    }
}
```

The reflection APIs return semantic objects. Macros compare types and
signatures structurally rather than comparing formatted source names.

## Requirement-providing macros

A declaration macro may promise that its expansion makes a type satisfy a
requirement:

```cx
macro Debug(target: type) -> declarations
    provides target: Debug {
    extension @{target} {
        fn write_debug(output: StringBuilder*) -> bool {
            // Generated implementation.
            return true;
        }
    }
}
```

This allows semantic matching to recognize macro-provided behavior while
expansion is being resolved. The promise is verified: if the generated
declarations do not actually satisfy `Debug`, compilation fails with a
diagnostic naming the false claim.

Parameterized requirements may bind macro arguments in the `provides` clause,
for example `provides target: Mapping<key, value>`.

## Generated syntax is ordinary CX

Expansion happens before the main type-inference and semantic phases. The
result then travels through the normal compiler pipeline:

```text
macro template + compile-time values
                ↓
          structured AST
                ↓
    scope, type, and call resolution
                ↓
       semantic validation and C
```

Consequently, macros cannot bypass the language:

- Generated calls must resolve.
- Returned expressions and elements must match their contracts.
- Generated public APIs must obey visibility rules.
- Duplicate names and invalid overloads are diagnosed.
- Requirement promises must be true.
- Compile-time-only nodes may not survive into C lowering.

## Diagnostics and origins

Use `compile_error(...)` to reject an unsupported macro input with a focused
message:

```cx
@if(!match.success) {
    compile_error(concat(
        "Debug cannot generate '",
        target.display_name,
        "'."
    ));
}
```

Generated nodes retain their template locations and macro invocation origin.
Diagnostics can therefore show both where invalid generated syntax came from
and which `use` expansion requested it. Nested compile-function failures also
include their compile-time call stack.

Recursive expansion is bounded and diagnosed rather than expanding forever.

## Current boundaries

- Macro expansion is compile-time-only; macros cannot be stored or called as
  runtime values.
- `use` must appear in a context compatible with the macro result contract.
- Typed expression macros produce exactly one expression; `elements<T>` is the
  separate contract for a sequence.
- Element-sequence macros currently target positional initializers.
- An empty `elements<T>` result can be spliced into an existing initializer,
  but cannot determine the size of a complete inferred fixed array.
- Macro inputs and splices are limited to compiler-supported typed values and
  syntax categories.
- Generated syntax must pass the same semantic checks as source-written CX.
- Attributes have no automatic derive behavior; a macro must interpret them.

## Related chapters

- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
- [Generics and requirements](12-generics-and-requirements.md)
- [Attributes and reflection](16-attributes-and-reflection.md)
- [Compile-time programming](17-compile-time-programming.md)
