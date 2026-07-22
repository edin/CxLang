using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Cx.Compiler;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class TranspileCommand : Command<TranspileCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Input .cx file or directory. If omitted, cx.toml is used.")]
        public string? InputPath { get; init; }

        [CommandOption("-o|--output <path>")]
        [Description("Output C file path.")]
        public string? OutputPath { get; init; }

        [CommandOption("--config <path>")]
        [Description("Project config path. Defaults to cx.toml in the current directory.")]
        public string? ConfigPath { get; init; }

        [CommandOption("--timings")]
        [Description("Print compiler phase timings.")]
        public bool Timings { get; init; }

    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var commandStarted = Stopwatch.GetTimestamp();
        var planStarted = Stopwatch.GetTimestamp();
        var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            settings.InputPath,
            settings.ConfigPath,
            settings.OutputPath,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));
        var planDuration = Stopwatch.GetElapsedTime(planStarted);
        if (!plan.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {plan.Error}");
            PrintTimings(settings.Timings, planDuration, null, null, commandStarted);
            return 2;
        }

        var compilerStarted = Stopwatch.GetTimestamp();
        var result = CliServices.Compile(plan.Value.SourceFiles);
        var compilerDuration = Stopwatch.GetElapsedTime(compilerStarted);
        if (!result.Success)
        {
            CliServices.PrintDiagnostics(result);
            PrintTimings(settings.Timings, planDuration, result, compilerDuration, commandStarted);
            return 1;
        }

        CliServices.PrintDiagnostics(result);
        var writeStarted = Stopwatch.GetTimestamp();
        CliServices.EnsureParentDirectory(plan.Value.COutputPath);
        File.WriteAllText(plan.Value.COutputPath, result.Output);
        var writeDuration = Stopwatch.GetElapsedTime(writeStarted);
        AnsiConsole.MarkupLineInterpolated($"[green]wrote[/] {plan.Value.COutputPath}");
        PrintTimings(settings.Timings, planDuration, result, compilerDuration, commandStarted, writeDuration);
        return 0;
    }

    private static void PrintTimings(
        bool enabled,
        TimeSpan planDuration,
        CompilationResult? result,
        TimeSpan? compilerDuration,
        long commandStarted,
        TimeSpan? writeDuration = null)
    {
        if (!enabled)
        {
            return;
        }

        AnsiConsole.WriteLine("timings:");
        PrintTiming("Project resolution", planDuration, indent: 1);
        if (result is not null)
        {
            foreach (var timing in result.Timings)
            {
                PrintTiming(timing.Name, timing.Duration, indent: 2);
            }
        }
        if (compilerDuration is not null)
        {
            PrintTiming("Compiler total", compilerDuration.Value, indent: 1);
        }
        if (writeDuration is not null)
        {
            PrintTiming("Output writing", writeDuration.Value, indent: 1);
        }
        PrintTiming("Command total", Stopwatch.GetElapsedTime(commandStarted), indent: 0);
    }

    private static void PrintTiming(string name, TimeSpan duration, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var milliseconds = duration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
        AnsiConsole.WriteLine($"{prefix}{name,-39} {milliseconds,10} ms");
    }
}
