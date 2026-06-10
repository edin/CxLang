using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class RunCommand : Command<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Input .cx file or directory. If omitted, cx.toml is used.")]
        public string? InputPath { get; init; }

        [CommandOption("--cc <compiler>")]
        [Description("C compiler executable.")]
        public string? Compiler { get; init; }

        [CommandOption("--cc-arg <arg>")]
        [Description("Additional argument passed to the C compiler. Can be repeated.")]
        public string[] CompilerArgs { get; init; } = [];

        [CommandOption("--program-arg <arg>")]
        [Description("Argument passed to the compiled program. Usually provided after '--'.")]
        public string[] ProgramArgs { get; init; } = [];

        [CommandOption("-o|--output <path>")]
        [Description("Executable output path.")]
        public string? OutputPath { get; init; }

        [CommandOption("--c-output <path>")]
        [Description("Generated C output path.")]
        public string? COutputPath { get; init; }

        [CommandOption("--config <path>")]
        [Description("Project config path. Defaults to cx.toml in the current directory.")]
        public string? ConfigPath { get; init; }

    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            settings.InputPath,
            settings.ConfigPath,
            settings.COutputPath,
            settings.OutputPath,
            settings.Compiler,
            settings.CompilerArgs));
        if (!plan.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {plan.Error}");
            return 2;
        }

        return CliServices.BuildNative(plan.Value, runAfterBuild: true, settings.ProgramArgs);
    }
}
