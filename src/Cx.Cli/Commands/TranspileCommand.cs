using System.ComponentModel;
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

    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            settings.InputPath,
            settings.ConfigPath,
            settings.OutputPath,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));
        if (!plan.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {plan.Error}");
            return 2;
        }

        var result = CliServices.Compile(plan.Value.SourceFiles);
        if (!result.Success)
        {
            CliServices.PrintDiagnostics(result);
            return 1;
        }

        CliServices.PrintDiagnostics(result);
        CliServices.EnsureParentDirectory(plan.Value.COutputPath);
        File.WriteAllText(plan.Value.COutputPath, result.Output);
        AnsiConsole.MarkupLineInterpolated($"[green]wrote[/] {plan.Value.COutputPath}");
        return 0;
    }
}
