using System.Diagnostics;
using Cx.Compiler;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;
using Spectre.Console;
using Tomlyn;
using Tomlyn.Model;

internal static class CliServices
{
    public static ResolvedBuildPlanResult ResolveStandardTestPlan(StandardTestPlanRequest request)
    {
        var baseDirectory = Environment.CurrentDirectory;
        var cOutputPath = ResolveOutputPath(
            request.COutputPath,
            configValue: null,
            Path.Combine("build", "c", "std.tests.c"),
            baseDirectory);
        var nativeOutputPath = ResolveOutputPath(
            request.NativeOutputPath,
            configValue: null,
            Path.Combine("build", "bin", "std.tests" + (OperatingSystem.IsWindows() ? ".exe" : "")),
            baseDirectory);
        var compiler = request.Compiler ?? "gcc";

        return ResolvedBuildPlanResult.Succeeded(new ResolvedBuildPlan(
            "std",
            ProjectKind.Executable,
            SourceFiles: [],
            EntryPoints: [],
            cOutputPath,
            nativeOutputPath,
            compiler,
            request.CompilerArgs,
            EnvPath: []));
    }

    public static ResolvedBuildPlanResult ResolveTestPlan(BuildPlanRequest request)
    {
        var config = ProjectConfig.Load(
            request.ConfigPath,
            useDefaultConfig: string.IsNullOrWhiteSpace(request.InputPath));
        if (config is { Success: false })
        {
            return ResolvedBuildPlanResult.Failed(config.Error);
        }

        var project = config.Value;
        var baseDirectory = project?.BaseDirectory ?? Environment.CurrentDirectory;
        var sourceEntries = !string.IsNullOrWhiteSpace(request.InputPath)
            ? [request.InputPath]
            : GetTestSourceEntries(project, baseDirectory);
        if (sourceEntries.Count == 0)
        {
            return ResolvedBuildPlanResult.Failed("Provide an input path, create cx.toml with sources, or add tests under a tests directory.");
        }

        var sources = ResolveSourceFiles(sourceEntries, baseDirectory);
        if (sources.Count == 0)
        {
            return ResolvedBuildPlanResult.Failed("No .cx source files were found.");
        }

        var name = project?.Name
            ?? Path.GetFileNameWithoutExtension(sources[0].Path)
            ?? "tests";
        var cOutputPath = ResolveOutputPath(
            request.COutputPath,
            configValue: null,
            Path.Combine("build", "c", name + ".tests.c"),
            baseDirectory);
        var nativeOutputPath = ResolveOutputPath(
            request.NativeOutputPath,
            configValue: null,
            Path.Combine("build", "bin", name + ".tests" + (OperatingSystem.IsWindows() ? ".exe" : "")),
            baseDirectory);
        var compiler = request.Compiler ?? project?.Compiler ?? "gcc";
        var compilerArgs = new List<string>();
        compilerArgs.AddRange(project?.CompilerArgs ?? []);
        compilerArgs.AddRange(request.CompilerArgs);
        var envPath = project?.EnvPath ?? [];

        return ResolvedBuildPlanResult.Succeeded(new ResolvedBuildPlan(
            name,
            ProjectKind.Executable,
            sources,
            EntryPoints: [],
            cOutputPath,
            nativeOutputPath,
            compiler,
            compilerArgs,
            envPath));
    }

    public static ResolvedBuildPlanResult ResolveBuildPlan(BuildPlanRequest request)
    {
        var config = ProjectConfig.Load(
            request.ConfigPath,
            useDefaultConfig: string.IsNullOrWhiteSpace(request.InputPath));
        if (config is { Success: false })
        {
            return ResolvedBuildPlanResult.Failed(config.Error);
        }

        var project = config.Value;
        var baseDirectory = project?.BaseDirectory ?? Environment.CurrentDirectory;
        var sourceEntries = !string.IsNullOrWhiteSpace(request.InputPath)
            ? [request.InputPath]
            : project?.Sources ?? [];
        if (sourceEntries.Count == 0)
        {
            return ResolvedBuildPlanResult.Failed("Provide an input path or create cx.toml with a sources array.");
        }

        var sources = ResolveSourceFiles(sourceEntries, baseDirectory);
        if (sources.Count == 0)
        {
            return ResolvedBuildPlanResult.Failed("No .cx source files were found.");
        }

        var name = project?.Name
            ?? Path.GetFileNameWithoutExtension(sources[0].Path)
            ?? "program";
        var defaultCOutput = Path.Combine("build", "c", name + ".c");
        var cOutputPath = ResolveOutputPath(
            request.COutputPath,
            project?.COutput,
            defaultCOutput,
            baseDirectory);
        var kind = project?.Kind ?? ProjectKind.Executable;
        var nativeOutputPath = ResolveOutputPath(
            request.NativeOutputPath,
            project?.Output,
            DefaultNativeOutput(kind, name),
            baseDirectory);
        var compiler = request.Compiler ?? project?.Compiler ?? "gcc";
        var compilerArgs = new List<string>();
        compilerArgs.AddRange(DefaultCompilerArgs(kind));
        compilerArgs.AddRange(project?.CompilerArgs ?? []);
        compilerArgs.AddRange(request.CompilerArgs);
        var envPath = project?.EnvPath ?? [];

        return ResolvedBuildPlanResult.Succeeded(new ResolvedBuildPlan(
            name,
            kind,
            sources,
            project?.EntryPoints ?? [],
            cOutputPath,
            nativeOutputPath,
            compiler,
            compilerArgs,
            envPath));
    }

    public static CompilationResult Compile(
        IReadOnlyList<SourceFile> sourceFiles,
        IReadOnlyList<string>? entryPoints = null) =>
        new CxCompiler().CompileToC(
            sourceFiles,
            new CEmissionOptions(EntryPoints:
                entryPoints is { Count: > 0 } ? entryPoints : null));

    public static CompilationResult CompileTests(IReadOnlyList<SourceFile> sourceFiles, string? moduleName = null) =>
        new CxCompiler().CompileTestsToC(sourceFiles, moduleName);

    public static CompilationResult AuditAst(IReadOnlyList<SourceFile> sourceFiles, bool includeStandardLibrary) =>
        new CxCompiler().AuditAstCompleteness(sourceFiles, includeStandardLibrary);

    public static CompilationResult AuditRawGenericUses(IReadOnlyList<SourceFile> sourceFiles) =>
        new CxCompiler().AuditRawGenericUses(sourceFiles);

    public static int BuildNative(
        ResolvedBuildPlan plan,
        bool runAfterBuild,
        IReadOnlyList<string> programArgs,
        bool buildTests = false,
        string? testModuleName = null)
    {
        if (runAfterBuild && plan.Kind == ProjectKind.Shared)
        {
            AnsiConsole.MarkupLine("[red]error:[/] Shared projects can be built but not run directly.");
            return 2;
        }

        var result = buildTests
            ? CompileTests(plan.SourceFiles, testModuleName)
            : Compile(plan.SourceFiles, plan.EntryPoints);
        if (!result.Success)
        {
            PrintDiagnostics(result);
            return 1;
        }

        PrintDiagnostics(result);
        EnsureParentDirectory(plan.COutputPath);
        EnsureParentDirectory(plan.NativeOutputPath);
        File.WriteAllText(plan.COutputPath, result.Output);

        var compileArgs = new List<string> { plan.COutputPath, "-o", plan.NativeOutputPath };
        compileArgs.AddRange(plan.CompilerArgs);
        compileArgs.AddRange(result.LinkerArguments);
        var compileExitCode = RunProcess(plan.Compiler, compileArgs, plan.EnvPath);
        if (compileExitCode != 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{plan.Compiler} failed[/] with exit code {compileExitCode}");
            AnsiConsole.MarkupLineInterpolated($"generated C kept at {plan.COutputPath}");
            return compileExitCode;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]built[/] {plan.NativeOutputPath}");
        return runAfterBuild
            ? RunProcess(plan.NativeOutputPath, programArgs, plan.EnvPath)
            : 0;
    }

    public static int RunProcess(
        string fileName,
        IReadOnlyList<string> args,
        IReadOnlyList<string>? prependPath = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (prependPath is { Count: > 0 })
        {
            var path = startInfo.Environment.TryGetValue("PATH", out var currentPath)
                ? currentPath
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, prependPath) + Path.PathSeparator + path;
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]failed to start[/] '{fileName}'");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]failed to start[/] '{fileName}': {ex.Message}");
            return 1;
        }
    }

    public static void PrintDiagnostics(CompilationResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            var color = diagnostic.Severity == DiagnosticSeverity.Warning ? "yellow" : "red";
            // A string interpolation is escaped by Spectre; formatting the object directly is not.
            var message = diagnostic.ToString();
            AnsiConsole.MarkupLineInterpolated($"[{color}]{message}[/]");
        }
    }

    public static int CreateProjectScaffold(string directory, string name, bool force)
    {
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(directory, "tests"));
        Directory.CreateDirectory(Path.Combine(directory, "build", "c"));
        Directory.CreateDirectory(Path.Combine(directory, "build", "bin"));

        var configPath = Path.Combine(directory, "cx.toml");
        if (File.Exists(configPath) && !force)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {configPath} already exists. Use --force to overwrite it.");
            return 2;
        }

        File.WriteAllText(configPath, CreateProjectConfig(name));

        var mainPath = Path.Combine(directory, "src", "main.cx");
        if (!File.Exists(mainPath))
        {
            File.WriteAllText(mainPath, CreateMainSource());
        }

        AnsiConsole.MarkupLineInterpolated($"[green]created[/] {configPath}");
        AnsiConsole.MarkupLineInterpolated($"[green]created[/] {mainPath}");
        AnsiConsole.MarkupLine("outputs: build/c/<name>.c and build/bin/<name>.exe");
        return 0;
    }

    private static string CreateProjectConfig(string name)
    {
        var executable = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        return $$"""
        name = "{{name}}"
        kind = "exe"
        sources = ["src"]

        c_output = "build/c/{{name}}.c"
        output = "build/bin/{{executable}}"

        cc = "gcc"
        cc_args = ["-O2"]
        """;
    }

    private static string CreateMainSource() =>
        """
        import c.stdio;

        fn main() -> int {
            printf("hello from cx\n");
            return 0;
        }
        """;

    private static string DefaultNativeOutput(ProjectKind kind, string name) =>
        kind == ProjectKind.Shared
            ? Path.Combine("build", "lib", name + SharedLibraryExtension())
            : Path.Combine("build", "bin", name + (OperatingSystem.IsWindows() ? ".exe" : ""));

    private static IReadOnlyList<string> DefaultCompilerArgs(ProjectKind kind)
    {
        if (kind != ProjectKind.Shared)
        {
            return [];
        }

        if (OperatingSystem.IsMacOS())
        {
            return ["-fPIC", "-dynamiclib"];
        }

        return OperatingSystem.IsWindows()
            ? ["-shared"]
            : ["-fPIC", "-shared"];
    }

    private static string SharedLibraryExtension() =>
        OperatingSystem.IsWindows()
            ? ".dll"
            : OperatingSystem.IsMacOS()
                ? ".dylib"
                : ".so";

    private static IReadOnlyList<string> GetTestSourceEntries(ProjectConfig? project, string baseDirectory)
    {
        var entries = new List<string>();
        entries.AddRange(project?.Sources ?? []);

        if (Directory.Exists(Path.Combine(baseDirectory, "tests")))
        {
            entries.Add("tests");
        }

        return entries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static IReadOnlyList<SourceFile> ResolveSourceFiles(
        IReadOnlyList<string> entries,
        string baseDirectory)
    {
        var files = new List<string>();
        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(entry, baseDirectory);
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.EnumerateFiles(path, "*.cx", SearchOption.AllDirectories));
                files.AddRange(Directory.EnumerateFiles(path, "*.cplus", SearchOption.AllDirectories));
                continue;
            }

            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .ToList();
    }

    private static string ResolveOutputPath(
        string? commandValue,
        string? configValue,
        string fallback,
        string baseDirectory)
    {
        var value = commandValue ?? configValue ?? fallback;
        return Path.IsPathRooted(value)
            ? value
            : Path.GetFullPath(value, baseDirectory);
    }
}
