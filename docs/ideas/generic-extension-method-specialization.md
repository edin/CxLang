---
status: captured
area: compiler
created: 2026-08-15
---

# Preserve a non-generic owner when specializing generic extension methods

## Motivation

A generic method declared inside an extension of a concrete type should
specialize only the method's type parameters. The owner type must remain the
concrete extension target.

## Desired behavior

```cx
extension PhpArguments {
    fn parse_if_present<T>(index: int, value: T*) -> bool {
        return !self.has(index) || self.parse(index, value);
    }
}
```

Calling this with an `i64*` should produce a function whose receiver remains
`PhpArguments*`:

```c
bool PhpArguments_parse_if_present_i64(
    PhpArguments* self,
    int index,
    i64* value);
```

## Current behavior

The C specialization currently also applies the method type argument to the
owner and emits a nonexistent receiver such as `PhpArguments_i64*`.

The PHP experiment uses explicit overloads as a behavior-preserving
workaround.

## Completion criteria

- Generic extension methods on concrete owners retain the concrete owner.
- Generic owners still receive their own declared type arguments.
- Specialization identity and C names distinguish method type arguments
  without inventing owner type arguments.
- Regression tests cover both concrete and generic extension owners.
