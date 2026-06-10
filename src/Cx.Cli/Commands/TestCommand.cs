using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class TestCommand : Command<TestCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Input .cx file or directory. If omitted, cx.toml sources plus tests/ are used.")]
        public string? InputPath { get; init; }

        [CommandOption("--cc <compiler>")]
        [Description("C compiler executable.")]
        public string? Compiler { get; init; }

        [CommandOption("--cc-arg <arg>")]
        [Description("Additional argument passed to the C compiler. Can be repeated.")]
        public string[] CompilerArgs { get; init; } = [];

        [CommandOption("-o|--output <path>")]
        [Description("Test executable output path.")]
        public string? OutputPath { get; init; }

        [CommandOption("--c-output <path>")]
        [Description("Generated test C output path.")]
        public string? COutputPath { get; init; }

        [CommandOption("--config <path>")]
        [Description("Project config path. Defaults to cx.toml in the current directory.")]
        public string? ConfigPath { get; init; }

        [CommandOption("--module <name>")]
        [Description("Run tests from one module. Use this to opt into std modules, for example --module std.core.")]
        public string? ModuleName { get; init; }

        [CommandOption("--std")]
        [Description("Run embedded std.core tests without requiring an input file or project.")]
        public bool StandardLibrary { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var plan = settings.StandardLibrary
            ? CliServices.ResolveStandardTestPlan(new StandardTestPlanRequest(
                settings.COutputPath,
                settings.OutputPath,
                settings.Compiler,
                settings.CompilerArgs))
            : CliServices.ResolveTestPlan(new BuildPlanRequest(
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

        var testModuleName = settings.StandardLibrary
            ? settings.ModuleName ?? "std.core"
            : settings.ModuleName;

        var testPlan = plan.Value with
        {
            COutputPath = settings.COutputPath is null
                ? Path.Combine(Path.GetDirectoryName(plan.Value.COutputPath) ?? string.Empty, plan.Value.Name + ".tests.c")
                : plan.Value.COutputPath,
            NativeOutputPath = settings.OutputPath is null
                ? Path.Combine(
                    Path.GetDirectoryName(plan.Value.NativeOutputPath) ?? string.Empty,
                    plan.Value.Name + ".tests" + (OperatingSystem.IsWindows() ? ".exe" : ""))
                : plan.Value.NativeOutputPath,
        };

        return CliServices.BuildNative(
            testPlan,
            runAfterBuild: true,
            programArgs: [],
            buildTests: true,
            testModuleName: testModuleName);
    }
}
