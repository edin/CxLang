---
status: exploring
area: resource-management
created: 2026-08-14
---

# Inferred resource ownership across function returns

## Motivation

CX already supports `using` bindings, reverse-order scope cleanup, cleanup on
control-flow exits, reassignment cleanup, and transfer when a `using` binding
is returned directly. Cleanup responsibility currently does not propagate to a
caller's ordinary `let` binding.

The goal is to make the existing resource model composable across function
calls without requiring callers to repeat `using` when the compiler already
knows that a function transfers ownership of its result.

## Desired behavior

```cx
fn create_object() -> Object {
    using result = Object.create();
    return result;
}

fn run() -> void {
    let object = create_object();
    // `object` owns the returned resource and is cleaned on scope exit.
}
```

The inferred binding should behave like an explicit `using` binding for
cleanup, reassignment, and return transfer:

```cx
fn forward_object() -> Object {
    let object = create_object();
    return object;
}
```

Ownership should also propagate through a direct return:

```cx
fn forward_directly() -> Object {
    return create_object();
}
```

## Proposed semantic rules

- Returning a cleanup-owning local transfers its ownership to the caller. The
  local is not disposed on that return path.
- A CX function whose result transfers cleanup responsibility has an internal
  owned-return semantic fact.
- Initializing a `let` binding from an owned-return call makes that binding
  cleanup-owning.
- Returning the result of an owned-return call propagates the owned-return fact
  through the calling function.
- Reassigning an inferred cleanup-owning binding follows the same ordering as
  explicit `using`: evaluate the replacement, clean the old value, then store
  the replacement.
- All value-returning paths must agree about ownership. Mixed owned and
  borrowed returns should produce a diagnostic rather than silently leak or
  double-clean a value.

Ownership is a semantic resource-flow fact. An inferred binding does not need
to be rewritten into a `UsingStatement` merely to represent that fact in the
AST.

## Initial constraints and non-goals

- Do not infer ownership transfer from `let second = first`. Supporting that
  safely would require moved-variable and use-after-move semantics.
- Do not infer ownership transfer through ordinary function arguments yet.
- Do not change CX into a strict aliasing or borrow-checked language. The goal
  is convenient deterministic cleanup, consistent with CX's C-like model.
- Extern functions have no body from which ownership can be inferred. They
  will require explicit ownership metadata if this behavior is needed.
- Separate compilation and exported ownership metadata can be designed later;
  the first implementation may operate on functions available to the current
  compilation.

## Possible implementation direction

The current cleanup lowering runs before semantic call resolution, while owned
return inference requires canonical resolved-call information. A clean pipeline
would be:

1. Perform structural pre-semantic lowering without scope cleanup lowering.
2. Resolve functions, overloads, calls, and types.
3. Infer function return ownership to a fixed point over the resolved call
   graph.
4. Infer cleanup responsibility for local bindings.
5. Lower explicit and inferred cleanup scopes.
6. Resolve or annotate the cleanup calls introduced by lowering.
7. Continue through validation and the Core CX runtime boundary.

The ownership model should be a dedicated semantic service rather than a set
of name-based checks inside `ScopeCleanupLowerer`.

## Open questions

- Should discarding an owned-return expression immediately dispose its result,
  or should it be diagnosed?
- What explicit syntax or metadata should describe ownership for extern
  functions and future separately compiled APIs?
- How should recursive functions participate in fixed-point inference?
- Should functions with no owned-return seed ever infer ownership solely from
  recursive calls?
- Should conditional expressions be able to combine owned results, and what
  diagnostic should mixed owned and borrowed branches produce?

## Completion criteria

- Owned return facts are represented independently of C lowering.
- Direct and transitive return transfer work through resolved calls.
- `let` bindings initialized by owned-return calls receive deterministic scope
  cleanup.
- Reassignment and all control-flow exits preserve existing cleanup ordering.
- Mixed ownership paths and unsupported extern cases have clear diagnostics.
- Tests cover nested scopes, early returns, conditionals, loops, reassignment,
  overloads, generics, and module boundaries.
- Core CX and the C backend consume only the already-lowered cleanup behavior;
  they do not infer ownership.

