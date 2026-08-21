# Projects, building, and testing

The `cx` command-line tool resolves source files, runs the compiler pipeline,
writes readable C, invokes a local C compiler, and can execute programs or CX
test runners. A project is an ordinary directory with a `cx.toml` file; a
single source file or directory can also be used without project configuration.

The repository root contains a working [`cx.toml`](../../cx.toml), and
[`examples/testing-guide.cx`](../../examples/testing-guide.cx) is a complete
test source.

## Installing or running the CLI

From the CX repository on Windows, install the current CLI for the user:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install-cx.ps1
```

Open a new terminal and confirm it is available:

```powershell
cx --help
```

Compiler developers can run the CLI directly from source without installing
it:

```powershell
dotnet run --project src/Cx.Cli -- --help
```

The examples below use `cx`. Replace that prefix with
`dotnet run --project src/Cx.Cli --` when working directly in this repository.

## Creating a project

Create a project directory and initial executable:

```powershell
cx new hello-cx
cd hello-cx
cx run
```

The generated layout is:

```text
hello-cx/
├── cx.toml
├── src/
│   └── main.cx
├── tests/
└── build/
    ├── c/
    └── bin/
```

`cx init` creates the same scaffold in the current directory. Use `--name` to
override the project name. Both `new` and `init` preserve an existing
`cx.toml` unless `--force` is supplied, and they preserve an existing
`src/main.cx` even in forced mode.

The initial program imports `c.stdio`, prints a greeting, and returns zero.

## Project configuration

When no input path is given, commands look for `cx.toml` in the current
directory:

```toml
name = "hello-cx"
kind = "exe"
sources = ["src"]
exclude = ["src/generated/**"]

c_output = "build/c/hello-cx.c"
output = "build/bin/hello-cx.exe"

cc = "gcc"
cc_args = ["-O2"]
```

Supported fields are:

| Field | Meaning |
| --- | --- |
| `name` | Project and default output base name |
| `kind` | `"exe"` or `"shared"` |
| `sources` | Required source files, directories, or glob patterns |
| `exclude` | Files, directories, or glob patterns removed from discovery |
| `entry_points` | Qualified reachability roots, required for shared projects |
| `c_output` | Generated C path |
| `output` | Native executable or shared-library path |
| `cc` | C compiler executable, defaulting to `gcc` |
| `cc_args` | Additional C compiler arguments |
| `env_path` | Directories prepended to `PATH` for the compiler and program |

The compatibility names `compiler` and `compiler_args` are also accepted, but
`cc` and `cc_args` are the canonical scaffold spelling.

Relative source and output paths are resolved from the directory containing
the selected configuration file. A legacy source entry may be one file or a
directory; directories are searched recursively for `.cx` and `.cplus` files.
Configured discovery also supports `*` within one path segment and recursive
`**` across zero or more directories:

```toml
sources = ["src/**/*.cx", "bindings/*.cplus"]
exclude = ["src/generated/**", "src/experimental/**"]
```

Exclude rules take precedence over includes. Duplicate matches compile each
physical source file once, and the final set is ordered deterministically.
Every include entry is required to match an existing file or directory;
malformed and unmatched patterns produce a project-resolution error. Both `/`
and `\` are accepted as configuration path separators.

Pass a different configuration explicitly when needed:

```powershell
cx build --config configs/release.toml
```

## Checking a project

`check` parses, expands, resolves, and semantically validates CX without
writing generated C:

```powershell
cx check
cx check examples/mvp.cx
```

It is the fastest normal command for editor-independent validation and CI
feedback. Diagnostics use source paths, line and column information, and
warning or error severity. A failed build returns a nonzero process exit code.

Compiler development also exposes two structural audits:

```powershell
cx check --ast-audit --include-std
cx check --generic-raw-audit
```

`--ast-audit` rejects parser error expressions that survived into the AST;
`--include-std` adds embedded standard-library sources to that audit.
`--generic-raw-audit` checks that generic-specialization discovery uses
structured types rather than textual fallback. These are compiler maintenance
gates, not requirements for ordinary application projects.

## Transpiling to C

Use `transpile` when the desired artifact is C source:

```powershell
cx transpile examples/mvp.cx
cx transpile examples/mvp.cx -o build/c/mvp.c
```

With a project, the configured `c_output` is used:

```powershell
cx transpile
```

The generated file is intended to be inspected, debugged, compiled by other
build systems, or checked into experimental output comparisons. It contains
ordinary C declarations, structs, functions, explicit cleanup, and native
calls rather than a hidden runtime bytecode format.

All commands that execute the compiler pipeline accept `--timings`:

```powershell
cx check --timings
cx transpile --timings
cx build --timings
cx run --timings
cx test --timings
```

The report separates project resolution, compile-time expansion, semantic and
lowering phases, C declaration pruning, emission, output writing, and total
command time as applicable. Native commands additionally report C compilation
and program or test execution. Timing reports use standard error, keeping
ordinary command, generated-program, and test output on standard output.

## Building native output

`build` performs the CX-to-C step and invokes the configured C compiler:

```powershell
cx build
cx build examples/mvp.cx
```

Override build paths or the compiler from the command line:

```powershell
cx build examples/mvp.cx `
    --c-output build/c/custom.c `
    --output build/bin/custom.exe `
    --cc gcc `
    --cc-arg=-O2
```

`--cc-arg` may be repeated. Command-line compiler arguments are appended after
project arguments. Link arguments discovered from C `declare` blocks are
appended automatically.

If native compilation fails, the generated C file is retained and its path is
reported. This makes it possible to inspect the exact translation unit and
reproduce the C compiler invocation independently.

## Running programs

`run` builds and then launches an executable:

```powershell
cx run
cx run examples/mvp.cx
```

Arguments after `--` are passed to the compiled program rather than parsed as
CX options:

```powershell
cx run -- first.txt --verbose
```

This is equivalent to repeated `--program-arg` options. The program's exit
code becomes the `cx run` exit code.

Directories listed in `env_path` are prepended to `PATH` for both the native C
compiler and the launched program. This is useful for project-local toolchains
or shared-library dependencies without changing the user's global environment.

## Executable entry points and pruning

An ordinary executable normally roots generated C reachability at `main` and
retains its transitive dependencies. Unused CX declarations can then be
removed from the emitted translation unit.

`entry_points` replaces the default roots with explicit free-function
identities:

```toml
entry_points = ["lib.alpha.start"]
```

Qualified names select the correct function even when several modules expose
the same source name. The selected function, its helpers, referenced types,
function values stored in metadata, and other transitive dependencies are
retained. An unknown entry point is a compiler diagnostic.

## Shared-library projects

Configure a native shared library with `kind = "shared"`:

```toml
name = "cx_demo"
kind = "shared"
sources = ["src"]
entry_points = ["php.binding.get_module"]
c_output = "build/c/cx_demo.c"
cc = "gcc"
cc_args = ["-O2"]
```

Shared projects must declare at least one entry point. These roots state which
CX functions form the externally reachable surface and prevent pruning from
discarding them.

`cx build` supplies the platform defaults and chooses a default extension:

| Platform | Default extension | Added compiler mode |
| --- | --- | --- |
| Windows | `.dll` | `-shared` |
| Linux | `.so` | `-fPIC -shared` |
| macOS | `.dylib` | `-fPIC -dynamiclib` |

Project `cc_args` should contain only additional library-specific options.
A shared project can be built but cannot be launched directly with `cx run`;
it must be loaded by its native host.

## CX test blocks

Tests use a language-level named block:

```cx
test "addition works" {
    let answer = 40 + 2;
    expect(answer == 42);
}
```

`cx test` collects test blocks, generates a native test runner, builds it, and
runs it:

```powershell
cx test examples/testing-guide.cx
```

For a configured project, testing includes the normal `sources` plus a
top-level `tests` directory when it exists:

```powershell
cx test
```

Application `build` and `run` do not include `tests` unless that directory is
listed explicitly in `sources`. Test blocks are consumed by test compilation;
they do not become production entry points.

Select tests from one module with:

```powershell
cx test --module app.model
```

Run the embedded standard-library suite independently:

```powershell
cx test --std
```

`--std` defaults to the `std.core` test module; `--module` can select another
embedded module. Test C and native outputs default to names ending in
`.tests.c` and `.tests` respectively, and can be overridden with `--c-output`
and `--output`.

## Generated output directories

Without explicit configuration, commands use conventional paths relative to
the project or current directory:

```text
build/c/<name>.c
build/bin/<name>[.exe]
build/lib/<name>.(dll|so|dylib)
```

Test artifacts use `<name>.tests.c` and `<name>.tests[.exe]`. Parent
directories are created automatically. Generated `build` contents are build
artifacts and should not be edited as source.

## VS Code and the language server

The extension under `editors/vscode` provides CX syntax highlighting, live
compiler diagnostics, and member completion. Packaged extensions include a
matching language server.

For extension development after compiler changes:

```powershell
dotnet publish src\Cx.Cli\Cx.Cli.csproj `
    -c Release `
    --no-self-contained `
    --output editors\vscode\server

cd editors\vscode
npm install
code .
```

Press `F5` to open an Extension Development Host, then open a `.cx` file or CX
project. `cx.languageServer.path` is only needed to override the bundled
server. The CLI command used by editor clients is `cx lsp`, which communicates
over standard input and output rather than as an interactive command.

## Repository verification

Compiler contributors should run the complete repository gate before handing
off a substantial change:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify.ps1
```

The gate:

1. builds the solution in Release mode;
2. runs the .NET compiler tests;
3. builds and runs embedded standard-library tests;
4. checks the configured project;
5. audits structured AST completeness;
6. audits generic-specialization discovery;
7. audits C backend dependency boundaries; and
8. checks the working diff for whitespace errors.

For focused iteration:

```powershell
dotnet build Cx.sln
dotnet test tests\Cx.Compiler.Tests\Cx.Compiler.Tests.csproj --no-restore
git diff --check
```

## Current boundaries

- Project configuration currently uses TOML and the fixed `cx.toml` default
  name.
- Project kinds are limited to executables and shared libraries.
- Native compilation requires an available external C compiler; `gcc` is the
  default.
- A shared project requires explicit entry points and cannot be run directly.
- The language server is experimental and currently focuses on diagnostics and
  completion rather than a complete IDE feature set.

## Related chapters

- [Modules and visibility](15-modules-and-visibility.md)
- [C interoperability](19-c-interop.md)
- [Resource management](14-resource-management.md)
