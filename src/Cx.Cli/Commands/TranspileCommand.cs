using System.ComponentModel;
using System.Diagnostics;
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
        using var timings = new CliTimings(settings.Timings);
        var planStarted = Stopwatch.GetTimestamp();
        var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            settings.InputPath,
            settings.ConfigPath,
            settings.OutputPath,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));
        var planDuration = Stopwatch.GetElapsedTime(planStarted);
        timings.RecordProjectResolution(planDuration);
        if (!plan.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {plan.Error}");
            return 2;
        }

        var compilerStarted = Stopwatch.GetTimestamp();
        var result = CliServices.Compile(plan.Value.SourceFiles, plan.Value.EntryPoints);
        var compilerDuration = Stopwatch.GetElapsedTime(compilerStarted);
        timings.RecordCompilation(result, compilerDuration);
        if (!result.Success)
        {
            CliServices.PrintDiagnostics(result);
            return 1;
        }

        CliServices.PrintDiagnostics(result);
        var writeStarted = Stopwatch.GetTimestamp();
        CliServices.EnsureParentDirectory(plan.Value.COutputPath);
        File.WriteAllText(plan.Value.COutputPath, result.Output);
        var writeDuration = Stopwatch.GetElapsedTime(writeStarted);
        timings.Record("Output writing", writeDuration);
        AnsiConsole.MarkupLineInterpolated($"[green]wrote[/] {plan.Value.COutputPath}");
        return 0;
    }
}
