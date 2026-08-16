# Resource management

CX uses explicit disposal plus scope-aware `using` bindings for deterministic
resource management. Cleanup is lowered into ordinary calls on every relevant
control-flow edge, preserving readable C while handling early returns, loop
control, reassignment, and ownership transfer.

The complete example for this chapter is
[`examples/resource-management.cx`](../../examples/resource-management.cx).

## Disposable values

A resource type provides an instance `dispose()` method, commonly declared
through `Disposable<T>`:

```cx
struct Resource: Disposable<Resource> {
    handle: void*;

    fn dispose() -> void {
        release(self.handle);
        self.handle = null;
    }
}
```

The standard requirement is structurally small:

```cx
requires Disposable<T> {
    fn dispose() -> void;
}
```

Its implicit receiver is `Self*`. The method can therefore mutate the resource
to clear handles, lengths, or ownership flags after releasing them.

Providing `dispose()` does not make every local automatic. Ordinary `let`
bindings remain manually managed. Automatic scope cleanup begins with `using`.

## Using bindings

Declare an owned scope resource with `using`:

```cx
fn write_message() -> bool {
    using buffer = ByteBuffer.with_capacity(64);
    buffer.write_bytes("hello", 5);
    return true;
}
```

At every exit from the binding's scope, CX inserts:

```cx
buffer.dispose();
```

The resource type must provide a compatible zero-argument instance
`dispose()` method. A `using` declaration also requires an initializer because
ownership must begin with a concrete value.

## Lexical ownership

The scope containing the `using` declaration owns the binding:

```cx
if (needed) {
    using temporary = ByteBuffer.with_capacity(16);
    process(&temporary);
} // temporary is disposed here
```

Cleanup occurs when control leaves that lexical scope, not merely at the end
of the whole function. Nested scopes therefore provide precise lifetime
boundaries without requiring heap ownership objects.

## Reverse declaration order

Multiple resources are cleaned in reverse order of acquisition:

```cx
using first = create_first();
using second = create_second();

// cleanup:
// second.dispose();
// first.dispose();
```

This mirrors stack unwinding and lets later resources safely depend on earlier
ones during their lifetime.

The rule applies independently within every nested scope.

## Early returns

Cleanup is inserted before an early return:

```cx
fn read() -> int {
    using buffer = ByteBuffer.with_capacity(32);

    if (!load(&buffer)) {
        return -1; // buffer is disposed first
    }

    return (int)buffer.length;
}
```

When the return expression reads the resource, CX first evaluates it into a
generated temporary, then performs cleanup, then returns the saved value:

```text
evaluate return value -> dispose resources -> return temporary
```

This prevents cleanup from invalidating data needed to calculate the result.

## Returning an owned resource

Returning the `using` binding itself transfers ownership to the caller:

```cx
fn create_buffer() -> ByteBuffer {
    using buffer = ByteBuffer.with_capacity(64);
    return buffer;
}
```

CX saves the value for return but deliberately does not dispose `buffer` in
this function. The caller becomes responsible for it:

```cx
using buffer = create_buffer();
```

Other resources in the same scope are still cleaned before the transfer:

```cx
using scratch = create_scratch();
using result = create_result();
return result; // cleans scratch, transfers result
```

Transfer is based on the resolved local binding, so an inner local shadowing
the same name does not accidentally transfer an outer resource.

## Reassigning an owned binding

Replacing a `using` resource disposes the old value:

```cx
using buffer = ByteBuffer.with_capacity(16);
buffer = ByteBuffer.with_capacity(128);
```

The order is carefully defined:

1. evaluate the replacement into a temporary;
2. dispose the old value;
3. assign the replacement temporary;
4. dispose the new value when its scope later exits.

Evaluating first matters when creation of the replacement reads the existing
resource:

```cx
resource = replace_resource(resource);
```

The old value remains valid throughout `replace_resource(resource)`.

## Break and continue

Resources declared inside a loop body are cleaned before leaving that
iteration:

```cx
while (next()) {
    using item = acquire();

    if (should_stop(item)) {
        break; // disposes item
    }

    if (should_skip(item)) {
        continue; // disposes item
    }
}
```

Cleanup respects the exact target scope. A `break` leaving an inner `switch`
cleans resources owned by that switch case but preserves resources belonging
to an enclosing loop that continues afterward.

## Try propagation

A propagating `try` is another early-return edge and performs pending cleanup:

```cx
fn load(success: bool) -> Result<int, Error> {
    using buffer = ByteBuffer.with_capacity(32);
    let value: int = try read_value(success);
    return Result.ok<int, Error>(value);
}
```

If `read_value` fails, CX disposes `buffer` before returning the propagated
error. Successful execution retains the resource until the normal scope exit.

A handled fallback does not exit the function:

```cx
let value = try read_value() ?? 0;
```

The surrounding resources remain alive because the error has been consumed.

## Generated C

`using` does not require a runtime cleanup stack. CX lowers ownership into
ordinary declarations, temporaries, disposal calls, and control flow before C
emission:

```cx
fn run() -> int {
    using buffer = ByteBuffer.with_capacity(8);
    return (int)buffer.capacity;
}
```

Conceptually becomes:

```c
ByteBuffer buffer = ByteBuffer_with_capacity(8);
int __cx_using_return_0 = (int)buffer.capacity;
Vec_dispose_u8(&buffer);
return __cx_using_return_0;
```

Generated temporary names are deterministic and collision-safe. Cleanup calls
are resolved semantically before reaching the C backend.

## Manual disposal

Use an ordinary `let` plus an explicit call when ownership does not align with
one lexical scope:

```cx
let buffer = ByteBuffer.with_capacity(32);
// transfer it elsewhere, store it, or conditionally retain it
buffer.dispose();
```

Do not also bind the same owned value with `using` unless its `dispose()` method
is intentionally idempotent; otherwise an explicit call followed by automatic
scope cleanup may release the resource twice.

CX does not currently provide borrow checking that proves two copied structs do
not own the same pointer. Ownership conventions remain important for manually
copied resource values.

## Disposable containers

Standard generic containers expose `dispose()` when their element or storage
semantics permit it. For example, `Option<T>` gains disposal when
`T: Disposable<T>`:

```cx
using maybe_buffer = Option.some<ByteBuffer>(buffer);
```

Its implementation disposes the contained success value only when present and
then clears `has_value`, preventing the same container instance from disposing
that value twice.

`Result<T, E>` currently gains `dispose()` when the success type `T` is
disposable. It disposes a present successful value and clears the result state.
Its current implementation does not dispose an error payload merely because
`E` is disposable.

Collection values such as `Vec<T>`, `Queue<T>`, `HashMap<K, V>`, and adapters
that expose their storage's disposal can all participate in `using`.

## Borrowed views

Not every pointer-carrying value owns its storage. `Slice<T>`, `Range<T>`, and
interface handles are borrowed views and should not release the referenced
object merely because the view's scope ends.

The type's API—not the presence of a pointer field—defines ownership. A
borrowed view normally has no disposal method, while an owning collection does.

## Resource management versus garbage collection

CX cleanup is deterministic and local:

- the release point is determined by lexical control flow;
- no tracing collector scans object graphs;
- no reference count is added to ordinary values;
- generated C contains direct cleanup calls;
- ownership transfer is explicit through returned values.

This model is well suited to files, buffers, allocators, locks, sockets, and C
library handles whose release timing matters.

## Current boundaries

- Only `using` bindings receive automatic cleanup; ordinary locals do not.
- A `using` declaration requires an initializer and a resolvable `dispose()`
  method.
- Resource structs still have C value-copy semantics; CX does not generally
  prevent duplicated owning handles.
- Manual disposal of a `using` binding can lead to double cleanup unless the
  resource guards against it.
- Ownership transfer currently recognizes a direct return of the owned local;
  more complex ownership movement should be made explicit.
- Borrowed lifetimes are not statically tracked.
- `Result<T, E>.dispose()` currently owns the successful `T` path, not a
  disposable `E` error path.

## Related chapters

- [Variables and constants](03-variables-and-constants.md)
- [Control flow](07-control-flow.md)
- [Arrays, slices, and iteration](10-arrays-slices-and-iteration.md)
- [Generics and requirements](12-generics-and-requirements.md)
- [Interfaces and adapters](13-interfaces-and-adapters.md)
