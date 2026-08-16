# Compile-time programming

CX has a typed evaluator for computing values, inspecting program structure,
selecting syntax, and reporting diagnostics while the program is compiled.
Compile-time execution transforms the CX AST before ordinary semantic analysis
and C lowering; it is not a runtime interpreter embedded in the executable.

The complete example is
[`examples/compile-time-programming.cx`](../../examples/compile-time-programming.cx).

## Compile-time constants

Declare an evaluator constant with `compile const`:

```cx
compile const prefix: string = "field_";
compile const suffix: string = "name";
compile const complete_name: string = concat(prefix, suffix);
```

The type and initializer are required. Constants may depend on earlier
compile-time constants and call compile-time operations. They are evaluated for
generation and omitted from emitted C.

Compile-time constants follow module visibility. A `public compile const` can
be imported by another module; a private one remains local to its module.

## Compile-time functions

Use `compile fn` for reusable evaluator logic:

```cx
compile fn generated_names() -> list<string> {
    let names: list<string> = [];
    names.add("first");
    names.add("second");
    return names;
}
```

Compile functions support typed parameters and return values, local bindings,
assignment, conditionals, loops, calls to other compile functions, and
recursion. Their types belong to the compile-time value model, including
`bool`, `int`, `string`, `name`, `type`, reflection objects, nullable values,
and `list<T>`.

```cx
compile fn optional_name(enabled: bool) -> string? {
    if (enabled) {
        return "generated";
    }

    return null;
}
```

Compile functions are not emitted into the executable. Public compile
functions can cross module boundaries through ordinary imports.

## Compile-time directives

Directives begin with `@` and control which AST nodes enter the runtime
program.

### Local evaluator bindings

`@let` evaluates an expression and binds its result for later directives:

```cx
@let members = target.fields;
@let count = members.count;
```

These bindings exist only during expansion. They can hold primitives, lists,
types, declarations, fields, functions, modules, attributes, and other
compile-time objects.

### Conditional generation

`@if` requires a compile-time Boolean and selects one syntax branch:

```cx
@if(T == int) {
    return 1;
} else {
    return 2;
}
```

The unselected branch is discarded before runtime semantic analysis. This is
why an unselected branch may contain a macro invocation that is unavailable in
the selected configuration.

### Repeated generation

`@foreach` evaluates a list and expands its body once per item:

```cx
@foreach name in generated_names() {
    fn @{as_name(name)}() -> int {
        return 21;
    }
}
```

Here `@{...}` splices the computed name into the surrounding syntax. The
executable example therefore generates `first()` and `second()` without a
textual source-generation pass.

Directives work at module level and inside supported declaration, statement,
and initializer contexts. Their bodies are parsed as structured CX syntax, so
commas, statements, declarations, and source locations remain AST data.

## Compile-time lists

Lists are mutable evaluator values:

```cx
compile fn selected_names() -> list<string> {
    let result: list<string> = [];
    result.add("id");
    result.add("name");
    return result;
}
```

Use `.count` to inspect a list and `.add(value)` to append a compatible value.
Lists may contain reflection objects and syntax as well as primitive values.
Nested and nullable list element types are supported.

A returned list is an evaluator value. It only becomes runtime syntax when a
directive iterates it or a splice places one of its values into an AST slot.

## Types as values

Types are first-class compile-time values:

```cx
@if(T == int) {
    // Generate the integer specialization.
}

@let pointer_type = Type.pointer(T);
@let signature = Type.from(fn(int) -> int);
```

The `Type` object can construct pointers, const types, arrays, generic
applications, and function types. Reflected types expose facts such as
`name`, `display_name`, `kind`, `element_type`, `type_arguments`, `fields`,
`methods`, enum members, and data-enum fields.

Prefer these structured type values over parsing or comparing rendered type
strings.

## Reflection objects

Compile-time code can inspect the program through typed objects:

```cx
@foreach field in target.fields {
    @let field_name = field.name;
    @let field_type = field.type;
}
```

Functions expose parameters, return types, signatures, attributes, visibility,
ownership, and callable references. Modules expose their name, public and local
functions, public and local types, and named type lookup:

```cx
@let api = module("api");
@foreach handler in api.public_functions {
    // Inspect or generate code for the handler.
}
```

Reflection observes semantic program objects, not arbitrary source text. The
[attributes chapter](16-attributes-and-reflection.md) covers metadata lookup in
detail.

## Requirements at compile time

Requirement matching is also available to the evaluator:

```cx
@let match = target.match(Disposable);

@if(match.success) {
    value.dispose();
}
```

`requirement_match(target, Requirement)` provides the same core operation and
also exposes inferred requirement type arguments. `satisfies` and
`declares_requirement` distinguish structural compatibility from an explicit
declaration when that distinction matters.

This allows generic code and macros to select valid generated operations using
the same requirement model as runtime semantic analysis.

## Compile-time diagnostics

Generation can reject invalid inputs deliberately:

```cx
@if(!match.success) {
    compile_error(concat(
        "Cannot generate cleanup for '",
        target.display_name,
        "'."
    ));
}
```

The diagnostic is attached to the expansion context. Failures inside nested
compile functions include a bounded compile-time call stack, and diagnostics
originating in a macro retain the macro invocation origin.

The evaluator enforces call-depth and step budgets so runaway recursion or
large computations fail with a diagnostic rather than hanging compilation.

## Expansion and specialization

Directives that depend only on immediately available values expand during the
main compile-time phase. Directives depending on a generic type parameter are
preserved until that function or type is specialized:

```cx
fn category<T>() -> int {
    @if(T == int) {
        return 1;
    } else {
        return 2;
    }
}
```

Each specialization receives only its selected runtime body. No `@if` remains
in generated C.

Macro declarations remain available in the compiler AST as reusable templates,
but their unexpanded bodies are not runtime code. Compiler analyses therefore
use executable traversal when they must ignore template-only expressions.

## Current boundaries

- Compile-time declarations and directives do not exist at runtime.
- Compile-time values must match the evaluator's typed value model; arbitrary
  runtime objects cannot be executed during compilation.
- `@if` conditions must evaluate to `bool`, and `@foreach` inputs must be
  iterable compile-time values.
- Every compile-time-only node must expand or produce a diagnostic before C
  lowering.
- Evaluation is intentionally bounded by call-depth and step limits.
- Reflection exposes compiler-supported semantic objects, not unrestricted
  mutation of the program AST.
- Generated syntax is validated by the normal semantic pipeline after
  expansion; successful evaluation does not make invalid generated CX valid.

## Related chapters

- [Generics and requirements](12-generics-and-requirements.md)
- [Modules and visibility](15-modules-and-visibility.md)
- [Attributes and reflection](16-attributes-and-reflection.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
