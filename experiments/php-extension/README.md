# CX PHP extension experiment

This experiment builds a PHP extension directly from CX without including PHP
headers in the generated C translation unit.

It currently exposes:

- `cx_answer(): int`
- `cx_greeting(): string`
- `cx_add(int $left, int $right): int`
- `cx_optional_sum(int $base, int $first = 1, int $second = 2): int`
- `cx_repeat(string $value, int $count = 1): string`
- `cx_multiply(float $left, float $right): float`
- `cx_not(bool $value): bool`
- `cx_weighted(int $count, float $factor, bool $enabled): float`
- `cx_checked_increment(int $value): int`, throwing PHP `Error` for negatives
- `cx_checked_repeat(string $value, int $count): string`, returning an owned result

The CX source is split into three layers:

- `src/php85_abi.cx` contains the targeted Zend layouts, runtime declarations,
  argument parsing, `zval` setters, and PHP-managed string allocation.
- `src/php_binding.cx` is the reusable binding layer. It contains only the
  `@export` marker, type policies, and the `PhpExport`/`PhpModule` macros.
  `PhpExport` reflects an ordinary typed CX function and generates its
  Zend-compatible wrapper, while `PhpModule` discovers `@export`
  functions and generates wrappers, exact-sized arginfo storage, function-table
  entries, and module registration.
- `src/main.cx` contains only the extension's application functions. Every PHP
  function uses `@export`; no application function receives Zend execution
  data or writes a `zval` directly.

The initial ABI description intentionally targets one environment:

- PHP 8.5 NTS
- PHP module API `20250925`
- Linux x86-64

Build and load it from WSL:

```bash
cd /mnt/d/Apps/CPlus/experiments/php-extension
cx build
php -n -d extension="$PWD/build/lib/cx_demo.so" \
  test.php
php -n -d extension="$PWD/build/lib/cx_demo.so" \
  stress.php
php -n -d extension="$PWD/build/lib/cx_demo.so" --ri cx_demo
```

The installed PHP development headers are used only as an ABI validation
oracle. The generated `build/c/cx_demo.c` does not include them.

`PhpArguments` provides indexed, overloaded parsing for integers, floating
point values, booleans, strings, Zend strings, and raw zvals. Shared-project
reachability starts directly from `get_module`; no synthetic `main` is needed.

The generated wrapper currently accepts any mixture of `i64`, `double`,
`bool`, and `StringView` parameters and returns. Type identity selects the local
representation, overloaded `PhpArguments.parse` selects input conversion, and
the return type selects the appropriate `ZendZval` setter.

A scalar function now needs only the marker attribute:

```cx
@export
fn cx_weighted(count: i64, factor: double, enabled: bool) -> double {
    return enabled ? (double)count * factor : 0.0;
}
```

The file-level `use PhpModule("cx_demo", "0.1.0");` configures the PHP-visible
module identity and discovers its exports. Parameter names, PHP type masks,
argument counts, wrapper references, and the function-table entry all come from
the reflected declarations.

Optional parameters carry a typed compile-time default. The binding derives
the PHP reflection text from the value's compile-time `display` property:

```cx
@export
fn cx_repeat(
    value: StringView,
    @optional(value: 1)
    @range(minimum: 0, maximum: 1000000)
    count: i64
) -> StringBuilder {
    // ...
}
```

The wrapper initializes `count`, accepts the derived `1..2` argument range,
uses `parse_if_present`, reports the PHP default through arginfo, validates the
range, copies the returned builder into a PHP string, and disposes the builder.
Exported optional parameters must be trailing; the binding reports a
compile-time diagnostic on any required parameter that follows one.
Defaults are type-checked by the binding: `i64` and `double` use compile-time
integers, `bool` uses a Boolean, and `StringView` uses a string. Optional string
views are initialized from that string before parsing a supplied PHP argument.

Exported functions may return `Result<i64, Error>` or
`Result<StringBuilder, Error>`. The generated wrapper returns the successful
value or throws a PHP `Error` containing the CX error message. Owned string
builders are copied into PHP memory and disposed on both normal and early-return
paths.
