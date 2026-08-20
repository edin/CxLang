using System.Diagnostics;
using System.Reflection;
using Cx.Compiler;
using Spectre.Console.Cli;

namespace Cx.Compiler.Tests;

public sealed class CliTimingTests
{
    public static TheoryData<Type> TimedCommandSettings => new()
    {
        typeof(TranspileCommand.Settings),
        typeof(CheckCommand.Settings),
        typeof(BuildCommand.Settings),
        typeof(RunCommand.Settings),
        typeof(TestCommand.Settings),
    };

    [Theory]
    [MemberData(nameof(TimedCommandSettings))]
    public void TimedCommands_ExposeTimingsOption(Type settingsType)
    {
        var property = settingsType.GetProperty("Timings");

        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property.PropertyType);
        Assert.Single(property.GetCustomAttributes<CommandOptionAttribute>());
    }

    [Fact]
    public void Reporter_UsesStableSharedFormatting()
    {
        using var writer = new StringWriter();
        var result = CompilationResult.Succeeded(string.Empty, []) with
        {
            Timings = [new CompilationTiming("User source parsing", TimeSpan.FromMilliseconds(1.25))],
        };
        var timings = new CliTimings(enabled: true, writer);
        timings.RecordProjectResolution(TimeSpan.FromMilliseconds(0.5));
        timings.RecordCompilation(result, TimeSpan.FromMilliseconds(2.5));
        timings.Record("Native compilation", TimeSpan.FromMilliseconds(3.75));

        timings.Write(TimeSpan.FromMilliseconds(10));

        var output = writer.ToString();
        Assert.Contains("timings:", output);
        Assert.Contains("  Project resolution", output);
        Assert.Contains("    User source parsing", output);
        Assert.Contains("1.25 ms", output);
        Assert.Contains("  Compiler total", output);
        Assert.Contains("  Native compilation", output);
        Assert.Contains("Command total", output);
    }

    [Fact]
    public void Reporter_WhenDisabled_WritesNothing()
    {
        using var writer = new StringWriter();
        var timings = new CliTimings(enabled: false, writer);

        timings.RecordProjectResolution(TimeSpan.FromMilliseconds(1));
        timings.Write(TimeSpan.FromMilliseconds(2));

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void CheckTimings_AreWrittenToStandardError()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = directory.WriteFile(
            "main.cx",
            "fn main() -> int { return 0; }");

        var result = RunCli("check", sourcePath, "--timings");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("checked", result.StandardOutput);
        Assert.DoesNotContain("timings:", result.StandardOutput);
        Assert.Contains("timings:", result.StandardError);
        Assert.Contains("User source parsing", result.StandardError);
    }

    [Fact]
    public void CheckTimings_OnFailure_IncludeCompletedCompilerPhases()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = directory.WriteFile("broken.cx", "fn main(");

        var result = RunCli("check", sourcePath, "--timings");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error:", result.StandardOutput);
        Assert.Contains("timings:", result.StandardError);
        Assert.Contains("User source parsing", result.StandardError);
        Assert.Contains("Compiler total", result.StandardError);
    }

    [Fact]
    public void RewriteRunProgramArgs_PreservesEveryProgramArgument()
    {
        var rewritten = CliApplication.RewriteRunProgramArgs([
            "run",
            "main.cx",
            "--timings",
            "--",
            "--flag",
            "two words",
            "[value]",
        ]);

        Assert.Equal([
            "run",
            "main.cx",
            "--timings",
            "--program-arg=--flag",
            "--program-arg=two words",
            "--program-arg=[value]",
        ], rewritten);
    }

    private static CliResult RunCli(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CliResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private sealed record CliResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "cx-cli-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string WriteFile(string name, string contents)
        {
            var path = Path.Combine(_path, name);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
