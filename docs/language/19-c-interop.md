# C interoperability

CX targets readable C and exposes C ABI declarations directly. Programs can
include headers, describe their contents with typed `declare` blocks, call
header-free external symbols, use opaque handles and function pointers, and
request platform-specific linker libraries.

The complete header declaration example is
[`examples/c-stdio-declare.cx`](../../examples/c-stdio-declare.cx). The smaller
[`examples/mvp.cx`](../../examples/mvp.cx) demonstrates a direct include plus
an external function prototype.

## Including C headers

System headers use angle brackets:

```cx
include <math.h>;
```

Project or relative headers use a string path:

```cx
include "native/widget.h";
```

These become ordinary C preprocessor includes:

```c
#include <math.h>
#include "native/widget.h"
```

An `include` alone makes the header visible to the generated C compiler. CX
still needs typed declarations for every symbol that CX source itself calls or
manipulates.

## Header-free external functions

Declare one external ABI function directly with `extern fn`:

```cx
include <math.h>;

extern fn sqrt(value: double) -> double;

fn hypotenuse(x: double, y: double) -> double {
    return sqrt(x * x + y * y);
}
```

The declaration participates in CX call checking and lowers to the external C
symbol rather than a CX function body.

An extern name represents one ABI symbol and therefore cannot be overloaded
with different signatures:

```cx
extern fn convert(value: int) -> int;
extern fn convert(value: char*) -> int; // error: one ABI symbol
```

Repeating an identical extern declaration is allowed. This accommodates
declarations reaching a compilation through more than one source path without
making an ABI name ambiguous.

## Typed header declarations

A `declare` block associates a header with the CX view of its contents:

```cx
declare <stdio.h> {
    type FILE = opaque;
    type size_t = usize;

    macro const EOF: int;

    fn printf(format: const char*, ...) -> int;
    fn fopen(path: const char*, mode: const char*) -> FILE*;
    fn fclose(stream: FILE*) -> int;
}
```

Members are header declarations, not definitions for CX to emit again. The
generated translation unit includes `stdio.h` and uses the header's original
C names.

Compared with a loose `include` plus several `extern fn` declarations, the
block records ownership of every type, constant, and callable. That lets the C
backend retain the correct header when one of its members is referenced and
discard an unused header declaration when safe.

System and quoted paths use the same distinction as `include`:

```cx
declare "native/widget.h" {
    type Widget = opaque;
    fn widget_create() -> Widget*;
}
```

## Opaque C types

Use `opaque` when CX needs the identity of a C type but must not depend on its
layout:

```cx
declare <stdio.h> {
    type FILE = opaque;
    fn fopen(path: const char*, mode: const char*) -> FILE*;
}
```

Opaque handles are normally passed by pointer. CX can type-check `FILE*`, null
checks, arguments, and results without inventing fields or a size for `FILE`.
The included C header supplies the real declaration.

## Header type aliases

A header block can describe a C typedef using a CX semantic representation:

```cx
declare <stdio.h> {
    type size_t = usize;
    fn fread(
        buffer: void*,
        size: size_t,
        count: size_t,
        stream: FILE*
    ) -> size_t;
}
```

CX checks uses through the alias while emitted C retains the header-facing
name where required. Structured aliases can also describe pointers, arrays,
generic CX types, and function types outside header blocks.

## C structs, enums, and raw unions

When layout is public, mirror it structurally inside the header block:

```cx
declare <native.h> {
    struct Point {
        x: int;
        y: int;
    }

    enum Mode {
        Read,
        Write
    }

    raw union Number {
        integer: int;
        decimal: double;
    }
}
```

These declarations describe types provided by the header. They are distinct
from ordinary CX declarations that cause a new C definition to be emitted.
Use an opaque type when the C library does not promise a public layout.

## C constants and macros

Header-provided objects are declared with `const`:

```cx
declare <native.h> {
    const native_version: int;
}
```

Preprocessor constants use `macro const`:

```cx
declare <stdio.h> {
    macro const EOF: int;
    macro const SEEK_END: int;
    macro const stdout: FILE*;
}
```

Both forms have a CX type. Marking a value as `macro` tells the compiler that
the name is supplied through C preprocessing rather than as an addressable
external object.

## C functions and function-like macros

Functions inside `declare` are external header functions without the `extern`
keyword:

```cx
declare <string.h> {
    fn strlen(text: const char*) -> usize;
}
```

Function-like C macros use `macro fn`:

```cx
declare <assert.h> {
    macro fn assert(condition: any) -> void;
}
```

The macro form preserves the header spelling at the call site so the C
preprocessor performs its normal expansion. Such declarations still give CX a
callable signature for argument checking.

## Variadic functions

Place `...` after at least one fixed parameter:

```cx
declare <stdio.h> {
    fn printf(format: const char*, ...) -> int;
}

printf("answer=%d name=%s\n", 42, "cx");
```

CX checks the fixed arguments. The remaining arguments follow the C variadic
ABI and its default-promotion rules at the generated C boundary.

The variadic marker must be last. A variadic function type also requires a
fixed parameter before `...`.

## Function pointer types

Function types map directly to C function pointers:

```cx
type Comparator = fn(const void*, const void*) -> int;
type PrintFn = fn(const char*, ...) -> int;
```

For example, CX can emit a typedef equivalent to:

```c
typedef int (*Comparator)(const void*, const void*);
```

Function values retain their full parameter and return types, so assignments
and callback arguments are checked structurally before lowering.

## Linking native libraries

Place `link` inside the header declaration that needs the library:

```cx
declare <math.h> {
    link "m";
    fn sqrt(value: double) -> double;
}
```

The compiler converts an ordinary library name to a linker argument such as
`-lm`. A string already beginning with `-` is passed through as an explicit
linker argument. Duplicate arguments are removed.

Restrict a dependency to one platform by placing its name before the library:

```cx
declare <math.h> {
    link linux "m";
}
```

Recognized current platform names are `windows`, `linux`, and `macos`.
Unqualified links apply on every platform.

Compile-time `@if` and `@foreach` are supported inside `declare` blocks, so a
library description can select declarations and links from compile-time
configuration without introducing runtime branches.

## C standard-library modules

CX packages common C declarations as ordinary modules:

```cx
import c.stdio;
import c.math;

fn main() -> int {
    printf("sqrt=%f\n", sqrt(25.0));
    return 0;
}
```

Modules such as `c.stdio`, `c.math`, `c.stdlib`, and `c.time` contain typed
`declare` blocks. Importing the CX module provides semantic visibility while
the underlying declaration retains its external ABI name. Module qualification
must never rename `printf`, `sqrt`, or another native symbol in generated C.

## Pointers and ABI responsibility

CX deliberately exposes C-shaped pointer operations:

```cx
let buffer: void* = null;
let text: const char* = "hello";
```

`const`, pointer depth, arrays, primitive widths, raw unions, and function
types are represented structurally and lowered without a hidden object ABI.
This makes generated declarations readable and compatible with C headers.

CX verifies the declarations it can see, but it cannot prove that a manually
written signature matches the linked C library. Incorrect calling conventions,
layouts, ownership rules, format strings, or platform type widths remain ABI
errors. Prefer a reviewed module containing one canonical declaration for each
native API.

## Reachability and emitted headers

Header declarations participate in C reachability. If runtime code uses a
type or symbol from a `declare` block, its header is retained. Entirely unused
header blocks can be omitted from generated C when unused-declaration stripping
is enabled.

Uses inside casts and `sizeof` also count. This prevents the optimizer from
dropping a header merely because its type appears outside a function call.

Direct `include` declarations are explicit and remain independent from this
typed member reachability model.

## Current boundaries

- One external ABI symbol cannot have several CX overload signatures.
- CX declarations must accurately match the native header and target ABI.
- Opaque types have no CX-visible layout and should be used through pointers.
- Variadic calls can only check the fixed parameter prefix.
- C preprocessor behavior remains in the downstream C compiler; CX does not
  evaluate arbitrary header macros.
- C macro constants and functions require explicit typed declarations before
  CX code can use them.
- Platform linking currently recognizes `windows`, `linux`, and `macos`.
- Native resource ownership is library-specific; a declaration does not infer
  which cleanup function must be called.

## Related chapters

- [Data types](02-data-types.md)
- [Functions and overloads](06-functions-and-overloads.md)
- [Tagged unions and matching](09-tagged-unions-and-matching.md)
- [Resource management](14-resource-management.md)
- [Modules and visibility](15-modules-and-visibility.md)
- [Compile-time programming](17-compile-time-programming.md)
