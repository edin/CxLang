# CX Session Handover

Last updated: 2026-08-15

## Start Here

Read `AGENTS.md` first. It contains the authoritative repository structure,
compiler pipeline, AST/semantic conventions, test style, and verification
commands.

The repository has a large, intentional, uncommitted working tree. Do not
reset, discard, or broadly rewrite it. Preserve unrelated changes and make
focused edits with `apply_patch`.

## Current Direction

CX is now a substantial typed, C-targeting language. We deliberately stopped
adding speculative language features and are validating/polishing the language
by building a real PHP extension directly in CX. The experiment is intended to
expose missing primitives and awkward compiler behavior through a demanding
real-world use case.

The current active project is:

`experiments/php-extension/`

It targets PHP 8.5 NTS on Linux x86-64 and generates a shared object without
including PHP headers in the generated C translation unit.

## PHP Extension Architecture

The experiment is split into three CX files:

- `src/php85_abi.cx` — PHP/Zend ABI declarations, `PhpArguments`, argument
  parsing, zval setters, PHP-managed allocation, and function/module helpers.
- `src/php_binding.cx` — reusable binding attributes, type policies,
  `PhpExport`, `PhpModule`, and currently the file-level `use PhpModule();`.
- `src/main.cx` — ordinary application functions marked with `@php_export`.

`cx.toml` declares a first-class shared project with `get_module` as its entry
point. CX reachability starts there; no synthetic `main` is required.

The binding layer currently supports parameters/returns involving:

- `i64`
- `double`
- `bool`
- `StringView`
- `StringBuilder` return values with copy-to-PHP and deterministic disposal

`PhpModule` reflects all `@php_export` functions and generates wrappers,
exact-sized arginfo storage, the function table, module entry, and
`get_module`.

## Most Recent Completed Slice: Optional PHP Parameters

Optional parameters are now represented with binding metadata:

```cx
@php_export
fn cx_repeat(
    value: StringView,
    @php_optional(value: 1, display: "1")
    @php_i64_range(minimum: 0, maximum: 1000000)
    count: i64
) -> StringBuilder {
    // ...
}
```

The generated wrapper:

- initializes the optional value;
- derives the accepted argument range (`1..2` here);
- parses it only when present;
- exposes the default text through PHP arginfo;
- validates the integer range and reports PHP `ValueError`;
- copies the returned builder into a PHP string;
- disposes the returned builder after the copy.

The compiler gained a general compile-time attribute metadata type named
`value`. It accepts any successfully evaluated `CompileTimeValue`. The parser,
formatter, semantic validation, diagnostics, and tests were updated for it.
The explicit `display` metadata intentionally avoids storing or reparsing raw
source text.

## Other Compiler Work Present in the Working Tree

The uncommitted work also includes support/fixes for:

- first-class shared projects and configured entry points;
- Linux solution/project publishing support;
- computed macro local names such as `let @{...}`;
- compile-time integer arithmetic;
- source type-alias resolution in the compile-time evaluator;
- named source types in compile-time type constructors;
- compile-time type equality preserving source/alias identity;
- semantic isolation between separate macro invocations;
- emitting C function declarations before globals that contain function
  pointers;
- keeping function references stored in generated metadata reachable.

Review the working diff before changing these areas; do not assume every file
is committed.

## Verification Status

At the end of the optional-parameter slice, all of the following passed:

- `scripts\verify.ps1`
- 876 compiler tests
- 49 embedded standard-library tests
- PHP extension build under WSL without warnings
- `test.php` smoke test
- `stress.php` with `calls=100000 memory_delta=0`
- `git diff --check`

The generated repeat wrapper was inspected and correctly disposes its owned
`StringBuilder` after copying the result into PHP memory.

Run the complete Windows gate after compiler changes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
```

Build and test the PHP extension in WSL:

```bash
cd /mnt/d/Apps/CPlus/experiments/php-extension
cx build
php -n -d extension="$PWD/build/lib/cx_demo.so" test.php
php -n -d extension="$PWD/build/lib/cx_demo.so" stress.php
php -n -d extension="$PWD/build/lib/cx_demo.so" --ri cx_demo
```

The WSL distro is `Ubuntu`. CX is installed under `/opt/cx` and exposed as
`/usr/local/bin/cx`. Windows and WSL builds share the checkout, so WSL restore
operations may alter shared `obj` state; restore/build the Windows solution
again before Windows tests if necessary.

## Known Issue

Moving `use PhpModule();` from `php_binding.cx` into `main.cx` caused the
configured build to report:

```text
Configured entry point 'get_module' does not name a free function.
```

This happened even with explicit source ordering and public macros/helpers.
Keeping the invocation in `php_binding.cx` works. This likely reveals a
cross-source macro expansion, declaration ownership, or module visibility
problem. It is not blocking the experiment, but it is a good compiler-polish
candidate because application composition ideally belongs outside the reusable
binding implementation.

## Recommended Next Work

Continue the PHP experiment rather than inventing unrelated features. A good
order is:

1. Add a clear diagnostic requiring optional exported parameters to be
   trailing. Cover required-after-optional and multiple optional parameters.
2. Generalize wrapper validation policies beyond the current
   `@php_i64_range`, while keeping them typed and attribute-driven.
3. Make module name/version configurable instead of hardcoded as `cx_demo` and
   `0.1.0`.
4. Investigate the cross-source `use PhpModule()` issue and move composition
   into `main.cx` or a small dedicated module file when fixed.
5. Extract `php85_abi.cx` and `php_binding.cx` into a reusable CX library only
   after their API stabilizes.
6. Add further PHP types and structured error/result handling based on actual
   wrapper needs.

Avoid introducing language-level default-argument semantics merely for PHP
bindings. The current metadata describes the foreign PHP API independently of
ordinary CX call semantics, which is intentional.

## Working Style for the Next Session

- Start with `git status --short` and inspect only relevant diffs.
- Prefer narrow tests first, then the complete verification gate.
- Keep transformations AST-based and typed; do not introduce source-string
  parsing or C-text intermediates.
- Treat `ProgramNode.Declarations` and `FunctionCatalog` as canonical.
- Keep compile-time-only nodes out of C lowering and retain explicit residue
  diagnostics.
- Do not stage or commit unless explicitly requested.

