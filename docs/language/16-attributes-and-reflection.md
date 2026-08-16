# Attributes and reflection

CX attributes are typed compile-time metadata. An attribute declaration defines
where the attribute may appear and the shape of its arguments; macros and
compile-time functions can then inspect that metadata through reflection.

The basic annotation example is
[`examples/attributes.cx`](../../examples/attributes.cx). A complete macro that
reflects over fields is
[`examples/macro-debug.cx`](../../examples/macro-debug.cx).

## Declaring attributes

An attribute names one or more targets after `on`:

```cx
attribute json_skip on field;

attribute route on fn {
    method: string;
    path: string;
}

attribute generated on struct, union, enum;
```

A fieldless attribute ends with `;`. An attribute with metadata fields uses a
body, and every application must supply every declared field.

Supported targets include `type_alias`, `extern`, `global`, `enum`, `variant`,
`struct`, `field`, `union`, `fn`, and `parameter`. The target is checked
semantically:

```cx
attribute range on field {
    min: int;
    max: int;
}

@range(min: 0, max: 100)
fn progress() -> int { // error: range cannot be applied to fn
    return 0;
}
```

Target names describe declaration categories; they do not introduce runtime
types or interfaces.

## Metadata types

Attribute schemas use compile-time metadata types rather than ordinary runtime
types:

| Type | Accepted value |
| --- | --- |
| `bool` | Compile-time Boolean |
| `int` | Compile-time integer |
| `string` | Compile-time string |
| `name` | Identifier-like compile-time name |
| `type` | Reflected CX type |
| `syntax` | Syntax node |
| `value` | Any compile-time value |
| `list<T>` | A list whose elements match `T` |

Lists may be nested and do not have a fixed length:

```cx
attribute metadata on field {
    aliases: list<string>;
    groups: list<list<string>>;
    generated_name: name;
}

struct Item {
    @metadata(
        aliases: ["id", "item_id"],
        groups: [["public"], ["storage", "indexed"]],
        generated_name: as_name("get_value")
    )
    value: int;
}
```

Runtime types such as `char*` are not valid schema types. Store a reflected
type with `type`, arbitrary evaluator data with `value`, or text with `string`.

## Applying attributes

Prefix a declaration with `@name`:

```cx
attribute json_name on field {
    name: string;
}

struct User {
    @json_name("displayName")
    name: char*;
}
```

Arguments may be positional, named, or positional followed by named:

```cx
@route("GET", path: "/users")
fn users() -> int {
    return 0;
}
```

The compiler diagnoses unknown attributes, invalid targets, missing or repeated
fields, excessive positional arguments, repeated applications, and metadata
type mismatches. Attribute names are declarations: `@derive` has no special
built-in meaning unless a `derive` attribute is declared.

Attribute arguments are compile-time expressions. They may call compile
functions and use the ordinary compile-time value model:

```cx
compile fn route_path(resource: string) -> string {
    return concat("/", resource);
}

@route(method: "GET", path: route_path("users"))
fn users() -> int {
    return 0;
}
```

The resulting metadata is evaluated and validated during compilation. It is
not emitted as a runtime object in generated C.

## Reflecting over attributes

Reflected declarations and members expose their attributes to compile-time
code. Use `has_attribute` when only presence matters:

```cx
macro Debug(target: type) -> declarations {
    extension @{target} {
        fn write_debug(output: StringBuilder*) -> bool {
            @foreach field in target.fields {
                @if(!has_attribute(field, "debug_skip")) {
                    // Generate code for this field.
                }
            }

            return true;
        }
    }
}
```

The equivalent object-oriented reflection surface is available through
`field.attributes` and `field.attribute("debug_skip")`. Named lookup returns
the attribute object or compile-time `null` when it is absent.

## Reading attribute fields

An attribute object's declared metadata fields are available as properties:

```cx
attribute route on fn {
    method: string;
    path: string;
}

macro RegisterRoutes(target: module) -> declarations {
    fn register_routes() -> void {
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
    }
}
```

The null check is important. Accessing a field on a missing attribute reports a
compile-time diagnostic because `null` is not an object-like value.

Attribute objects also expose their name and argument syntax. The lower-level
`attributes(node)` and argument reflection intrinsics remain useful when a
macro needs to process metadata generically rather than knowing its schema in
advance.

## Constructing and transforming attributes

Macros can construct metadata with `Attribute.create` and attach it while
building or transforming syntax:

```cx
@let marker = Attribute.create("generated");
@let updated = parameter.add_attribute(marker);
```

`with_attributes(...)` replaces a supported syntax object's attribute list;
`add_attribute(...)` appends one attribute while preserving its other syntax
and source metadata. Constructed attributes pass through the same semantic
validation as source-written applications.

This lets one macro leave typed metadata for a later expansion without
encoding compiler state in strings or comments.

## Attributes as macro policy

Attributes describe intent; macros decide what that intent generates. The
[`macro-debug.cx`](../../examples/macro-debug.cx) example combines them:

```cx
struct User {
    id: int;

    @debug_skip
    internal_code: int;

    use Debug(Self);
}
```

`Debug` reflects over `User.fields`, skips fields carrying `debug_skip`, checks
each remaining field against a structural requirement, and emits a typed
extension method. No runtime reflection table or attribute branch remains in
the C output.

## Current boundaries

- Attribute schemas contain compile-time metadata types, not arbitrary runtime
  CX types.
- Every declared metadata field is required; schema-level default values are
  not currently part of attribute declarations.
- The same attribute cannot be applied more than once to one declaration.
- Attribute lookup is compile-time-only and produces no automatic runtime
  metadata.
- Attributes do not generate behavior by themselves; a macro or compile-time
  consumer must interpret them.
- There is no built-in `derive` attribute.
- Applying attributes to imports, module declarations, C declaration blocks,
  requirements, and macro declarations is currently rejected by the parser.

## Related chapters

- [Structs](05-structs.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Generics and requirements](12-generics-and-requirements.md)
- [Modules and visibility](15-modules-and-visibility.md)
- [Constructors, initializers, and typed AST macros](../features/initializers-and-typed-macros.md)
