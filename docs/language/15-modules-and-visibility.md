# Modules and visibility

CX modules provide logical ownership, imports, visibility boundaries, and
collision-safe semantic identity across files. Module information survives
projection and specialization even when generated C keeps simple unqualified
names where no collision exists.

The complete example for this chapter is
[`examples/modules-and-visibility.cx`](../../examples/modules-and-visibility.cx).

## File-scoped modules

A file can assign all its declarations to one module:

```cx
module app.main;

fn main() -> int {
    return 0;
}
```

The dotted name is one logical module identity. It is not a sequence of nested
source blocks or C namespaces.

Several physical files may declare the same module. Their declarations are
merged into one semantic module:

```cx
// first file
module lib.math;

fn helper() -> int {
    return 40;
}
```

```cx
// second file
module lib.math;

public fn answer() -> int {
    return helper() + 2;
}
```

Private declarations are accessible across files belonging to the same module.
The file boundary does not create a second visibility boundary.

## Module blocks

A single source file can contain several independent modules:

```cx
module app.main {
    import lib.values;

    fn main() -> int {
        return value();
    }
}

module lib.values {
    public fn value() -> int {
        return 42;
    }
}
```

Module blocks are especially useful for tests and compact examples. The
compiler projects each block as an ordinary independent module unit before
visibility analysis and program merging.

A file cannot mix a file-scoped `module name;` declaration with module blocks.
When module-block form is used, declarations cannot remain outside the blocks,
and modules cannot be nested inside other modules.

## Plain imports

Import a module by its full name:

```cx
import lib.math;
```

Its public symbols become available to the importing module. They can be used
through normal imported lookup, and a fully qualified reference remains
available when ownership should be explicit:

```cx
let first = answer();
let second = lib.math.answer();
```

Only the requested module is visible. Importing one module does not implicitly
make that module's private dependencies available.

## Qualified imports

Give a module a local alias when qualification improves clarity or avoids
collisions:

```cx
import lib.math as math;

let point: math.Point = math.origin();
let answer = math.answer(point);
```

The alias applies to types, functions, globals, compile-time symbols, and other
public declarations from that module. A qualified import does not also leak
those names unqualified into the importing scope.

Semantic resolution supports these qualified identities. The current C backend
still has a known gap for some calls to CX-defined functions through a module
alias: the dotted source name can survive into emitted C. Qualified imports of
C extern modules lower through their ABI identities, while CX-defined calls
should use a plain or selective import until that backend gap is closed.

Import aliases are local to their containing module. Two modules in one source
file may use the same alias for different targets without sharing state.

## Selective imports

Import individual symbols with `from`:

```cx
from lib.math import answer, Point;
```

Each selected symbol can be renamed:

```cx
from lib.values import offset as adjustment;
```

This introduces only the selected public names. It is useful when a module has
a broad API but the caller needs a small, explicit subset.

Qualified and selective imports may coexist:

```cx
import c.math as math;
from c.math import sqrt as square_root;
```

## Public and private declarations

Top-level declarations are private to their module unless marked `public`:

```cx
module lib.math;

fn hidden_base() -> int {
    return 40;
}

public fn answer() -> int {
    return hidden_base() + 2;
}
```

`answer` is callable by importers. `hidden_base` remains available throughout
`lib.math`, including its other files, but cannot be called from another
module.

The same rule applies to types and globals:

```cx
public struct Point {
    x: int;
    y: int;
}

public const origin_x: int = 0;
```

Public extern declarations and public compile-time declarations also retain
their respective runtime or compile-time identities when imported.

## Imports are not re-exports

An import controls what the current module can see. It does not add the
dependency's symbols to the current module's public API:

```cx
module graphics;

import c.stdio;
```

A caller importing `graphics` cannot use `printf` merely because `graphics`
uses it internally. The caller must import `c.stdio` itself.

For the same reason, `public import` is rejected. Public API is expressed
through public declarations, not transitive namespace leakage.

## Public API type safety

A public declaration cannot expose a private type in its signature:

```cx
module lib.model;

struct Hidden {
    value: int;
}

public fn reveal(value: Hidden) -> Hidden { // error
    return value;
}
```

Even if callers could name `reveal`, they could not legally name its parameter
or result type. CX reports this at the public declaration rather than allowing
an unusable API.

The check applies recursively through structured type syntax such as pointers,
arrays, function types, and generic arguments.

## Types across module boundaries

Imported types retain their defining module identity:

```cx
import lib.model as model;

fn consume(value: model.Item) -> int {
    return value.value;
}
```

Two modules may each declare `Item`; they remain different semantic types even
after all declarations are projected into the root compilation program.

Type resolution, assignment analysis, return flow, enum lookup, union matching,
interface matching, requirements, and overload resolution all use that module
identity rather than choosing the first source name found.

## Functions and overloads across modules

Functions retain canonical identity including their module. Imports contribute
visible candidates to the function catalog, but unrelated private functions do
not enter the caller's overload set.

When two imported modules expose the same function name, qualify the call:

```cx
import lib.first;
import lib.second;

let total = lib.first.value() + lib.second.value();
```

The generated C mangler uses module qualification when required to avoid a
collision, producing names such as:

```c
lib_first_value();
lib_second_value();
```

When no collision exists, CX may keep the simpler C name. A module declaration
does not mechanically prefix every emitted symbol.

## Extensions across files

Declarations contributing to the same module are merged before semantic
resolution. This allows one file to define a type and another file in that
module to add qualified methods or extensions:

```cx
module app.model;

struct Counter {
    value: int;
}
```

```cx
module app.model;

fn Counter.increment(amount: int) -> void {
    self.value += amount;
}
```

Standard-library modules use the same mechanism to combine core types and
their behavior from several embedded sources.

Cross-module access to a type or method still obeys visibility; merging is not
a bypass for private ownership.

## Module visibility and macros

Compile-time functions, constants, macros, reflected modules, and generated
declarations use the same module visibility model as runtime symbols.

A compile-time function imported through an alias resolves in the caller's
module context. Reflected module properties expose public functions and public
types rather than leaking private implementation declarations. Macro-generated
declarations retain the module where expansion places them.

This keeps compile-time generation from becoming an accidental visibility
escape hatch.

## Standard-library modules

CX's embedded library uses named modules such as:

```text
std.core
std.bitmap
c.stdio
c.math
c.stdlib
```

Core language support and the embedded core library are loaded by the compiler
pipeline. Optional standard modules and C declarations are imported explicitly
where needed:

```cx
import std.bitmap;
import c.stdio;
```

The `c.*` modules provide typed declarations for C APIs; importing them does
not change their external ABI symbol names.

## Modules and physical projects

Module identity is semantic and can come from source declarations or configured
project ownership. A project may load several files into one module, or source
files may state their module explicitly.

Configured entry points are resolved by module and function identity. This
allows separate modules to contain same-named public functions without making
the selected executable entry point ambiguous.

Project configuration and CLI workflows will be covered in the projects,
building, and testing chapter.

## Current boundaries

- Module blocks cannot be nested.
- File-scoped module declarations and module blocks cannot be mixed in one
  source file.
- Declarations outside module blocks are rejected when block form is used.
- Imports are private dependencies and cannot be declared `public`.
- Only public declarations cross module boundaries.
- A public API cannot expose a private type.
- Qualified import aliases do not create unqualified symbol aliases.
- Some qualified calls to CX-defined module functions still require a backend
  lowering fix; plain and selective imports avoid the dotted-name residue.
- C names are module-qualified when disambiguation requires it, not necessarily
  for every declaration.

## Related chapters

- [Data types](02-data-types.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Methods, extensions, operators, and conversions](11-methods-extensions-operators-and-conversions.md)
- [Generics and requirements](12-generics-and-requirements.md)
- [Interfaces and adapters](13-interfaces-and-adapters.md)
