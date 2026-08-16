# Interfaces and adapters

CX provides two complementary abstraction tools. Interfaces create runtime
values containing state and a typed function table. Adapters create distinct
source-level views over an existing storage type, selectively exposing or
renaming behavior without adding a wrapper object.

The complete example for this chapter is
[`examples/interfaces-and-adapters.cx`](../../examples/interfaces-and-adapters.cx).

## Declaring an interface

An interface lists the instance operations available through an interface
value:

```cx
interface Reader {
    fn read() -> int;
}
```

Like owned and requirement methods, an interface method has an implicit
receiver. Callers see only the declared parameters:

```cx
fn consume(reader: Reader) -> int {
    return reader.read();
}
```

The interface declaration describes runtime dispatch. It does not provide
field storage or method bodies.

## Implementing an interface

A struct declares an implemented interface after `:` and provides compatible
methods:

```cx
struct CounterReader: Reader {
    value: int;

    fn read() -> int {
        return self.value;
    }
}
```

The compiler validates the implementation against the interface method slots.
Method names, parameter types, and result types must match after receiver and
generic substitution.

Methods may be owned by the struct or supplied through extensions. Interface
binding uses the canonical resolved implementation rather than searching by
name during C emission.

## Creating an interface value

A concrete value can convert to an interface it declares:

```cx
let concrete = CounterReader { value: 42 };
let reader: Reader = concrete;
```

CX inserts this binding in expected-type positions, including typed
initializers, assignments, arguments, and returns:

```cx
fn forward(value: CounterReader) -> Reader {
    return value;
}

consume(concrete);
```

A struct that does not implement the interface is rejected rather than being
accepted merely because a similarly named method exists.

Aliases of implementing types retain the same valid binding.

## Runtime representation

An interface value is a small C-friendly handle. Conceptually it contains:

```c
typedef struct Reader {
    void* state;
    const ReaderVTable* vtable;
} Reader;
```

The generated vtable contains one typed function pointer per interface method.
For `CounterReader`, CX emits a table referring to the concrete implementation
and constructs the interface value conceptually as:

```c
(Reader) {
    .state = &concrete,
    .vtable = &CounterReader_Reader_vtable
}
```

An interface call reads the function pointer from the vtable and passes the
stored state. Ordinary binding requires no heap allocation. CX also emits a
compact `CxTypeId` in each vtable for interface `match`; this is a generated C
enum, not heap-backed reflection metadata.

## State, copying, and lifetime

Copying an interface copies its handle, not the concrete state it references.
Several interface values can therefore dispatch to the same underlying struct.

The concrete state must outlive every interface handle that refers to it. An
interface created from a local value must not escape into a context that uses
it after that local's lifetime. CX's C-facing model does not add tracing,
reference counting, or automatic heap promotion.

Mutating methods operate on the referenced concrete state, so changes remain
visible through the original value and other handles bound to it.

## Interface pointers

An interface value may itself be addressed and passed as `Reader*`:

```cx
fn inspect(reader: Reader*) -> int {
    return reader.read();
}
```

This points to the interface handle, whose `state` still points to the concrete
implementation. It is distinct from a raw pointer to the concrete struct.

Interface declarations are emitted before structs containing interface fields
or pointers, so ordinary C declaration ordering remains valid.

## Matching interface implementations

`match` can inspect the concrete implementation stored in an interface value:

```cx
fn classify(reader: Reader) -> int {
    match reader {
        CounterReader: counter => {
            return counter.value;
        }
        _ => return 0;
    }
}
```

The arm binding is a pointer to the concrete implementation—in this case
`CounterReader*`. A match also works through a pointer to the interface handle.

CX diagnoses an arm naming a known type that does not implement the matched
interface. Unknown and duplicate arms are diagnosed as well.

Unlike a tagged union, the set of interface implementations is open. Use `_`
when unmatched implementations need a fallback.

## Interfaces versus requirements

Interfaces and requirements answer different questions:

| Property | Requirement | Interface |
| --- | --- | --- |
| Checked | During specialization/analysis | During implementation and binding |
| Runtime value | No | Yes |
| Dispatch | Concrete specialized call | Function table |
| Can require fields | Yes | No runtime field exposure |
| Implementation set | Structural | Explicitly declared |
| Primary use | Generic constraints and protocols | Heterogeneous runtime values |

Use a requirement when generic code should specialize for a concrete type. Use
an interface when values of different concrete types must travel through one
runtime type and dispatch dynamically.

## Declaring an adapter

An adapter gives a storage type a distinct semantic name and API:

```cx
type Counter using CounterStorage {
}
```

`Counter` has the fields and physical representation of `CounterStorage`, but
its visible behavior is controlled by the adapter declaration. It is more than
a transparent alias: it can expose, rename, retarget, and add methods.

Generic adapters substitute their parameters into the storage type:

```cx
type Stack<T> using Vec<T> {
}
```

`Stack<int>` uses `Vec<int>` storage while remaining a distinct CX type.

## Exposing instance methods

Expose a storage method unchanged:

```cx
type Buffer using ByteBuffer {
    expose dispose;
}
```

Or rename it for the adapter's domain:

```cx
type Stack<T> using Vec<T> {
    expose add as push;
    expose pop;
}
```

Calls resolve through the adapter API and lower directly to the underlying
storage method:

```cx
stack.push(42);
```

Conceptually becomes a call such as `Vec_add_int(&stack, 42)`. No forwarding
object or runtime trampoline is emitted.

If the source name has several overloads, `expose` preserves the complete
overload set. Normal argument ranking chooses among the exposed signatures,
and equal candidates produce an ambiguity diagnostic.

## Exposing static functions

Mark static exposure explicitly:

```cx
type Counter using CounterStorage {
    expose static create;
}
```

The source static function may return the storage type. Add `-> Self` to expose
it as a factory returning the adapter type:

```cx
type Counter using CounterStorage {
    expose static create -> Self;
}

let counter: Counter = Counter.create(42);
```

This is a semantic return retargeting over compatible storage, not a runtime
conversion or copy.

Static exposures can also be renamed:

```cx
type View<T> using Storage<T> {
    expose static create as make;
}
```

## Adapter-owned methods

An adapter can define behavior alongside its exposures:

```cx
type Counter using CounterStorage {
    expose increment as add;

    fn current() -> int {
        return self.value;
    }
}
```

`self` uses the adapter's storage representation, so owned methods can access
the underlying fields directly and call other exposed methods:

```cx
fn add_twice(amount: int) -> int {
    self.add(amount);
    return self.add(amount);
}
```

## Chained adapters

An adapter may use another adapter as its storage view:

```cx
type ByteBuffer using Vec<u8> {
    expose add as write_u8;
}

type StringBuilder using ByteBuffer {
    expose write_u8;
}
```

CX resolves the complete chain to the ultimate storage method. A call to
`StringBuilder.write_u8` still lowers directly to the specialized `Vec<u8>`
implementation.

Static `-> Self` retargeting composes through chains as well, allowing each
layer to expose factories returning its own semantic type while retaining the
same physical storage.

## Adapter representation

Adapters do not emit nested wrapper structs. If `Stack<int>` uses `Vec<int>`, a
local stack is represented by the specialized vector C type:

```c
Vec_int stack;
```

Field lookup, initialization, function arguments, tagged-union payloads, and
implicit conversion matching normalize through the adapter's storage type when
physical compatibility is required. The adapter identity remains available to
semantic method lookup and diagnostics.

This separation provides a zero-cost domain API without losing type-directed
behavior in CX source.

## Adapter and interface roles

Adapters and interfaces may both make an API smaller, but they do so at
different stages:

- an adapter statically selects a view over one known storage representation;
- an interface dynamically selects behavior for one of several implementing
  concrete types.

Adapters have no vtable. Interfaces do not automatically inherit all methods
or fields from their concrete state.

## Current boundaries

- Interface values borrow concrete state; CX does not automatically extend its
  lifetime.
- Interfaces themselves do not currently take generic type parameters.
- Interface declarations contain instance slots rather than static methods or
  stored fields.
- Interface implementations must be declared explicitly on the concrete type.
- Interface calls use runtime function-table dispatch.
- Interface matches should include `_` when future implementations need a
  fallback.
- Adapters preserve storage rather than wrapping it; they cannot add independent
  instance fields beyond the storage type.
- Only explicitly exposed storage methods are part of an adapter's forwarded
  API.
- Exposed overloads remain overloads and may still be ambiguous for a call.

## Related chapters

- [Structs](05-structs.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Tagged unions and matching](09-tagged-unions-and-matching.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Generics and requirements](12-generics-and-requirements.md)
