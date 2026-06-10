using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class CheckCommand : Command<CheckCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[input]")]
        [Description("Input .cx file or directory. If omitted, cx.toml is used.")]
        public string? InputPath { get; init; }

        [CommandOption("--config <path>")]
        [Description("Project config path. Defaults to cx.toml in the current directory.")]
        public string? ConfigPath { get; init; }

        [CommandOption("--ast-audit")]
        [Description("Fail if the parser falls back to raw expression nodes.")]
        public bool AstAudit { get; init; }

        [CommandOption("--c-raw-audit")]
        [Description("Report raw C escapes remaining in the lowered C AST.")]
        public bool CRawAudit { get; init; }

        [CommandOption("--generic-raw-audit")]
        [Description("Report generic specializations still discovered through text fallback.")]
        public bool GenericRawAudit { get; init; }

        [CommandOption("--include-std")]
        [Description("Include embedded standard library files in --ast-audit.")]
        public bool IncludeStandardLibrary { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
            settings.InputPath,
            settings.ConfigPath,
            COutputPath: null,
            NativeOutputPath: null,
            Compiler: null,
            CompilerArgs: []));
        if (!plan.Success)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {Markup.Escape(plan.Error)}");
            return 2;
        }

        var result = settings.GenericRawAudit
            ? CliServices.AuditRawGenericUses(plan.Value.SourceFiles)
            : settings.CRawAudit
            ? CliServices.AuditRawC(plan.Value.SourceFiles)
            : settings.AstAudit
                ? CliServices.AuditAst(plan.Value.SourceFiles, settings.IncludeStandardLibrary)
                : CliServices.Compile(plan.Value.SourceFiles);
        if (!result.Success)
        {
            CliServices.PrintDiagnostics(result);
            return 1;
        }

        CliServices.PrintDiagnostics(result);
        if (settings.CRawAudit || settings.GenericRawAudit)
        {
            AnsiConsole.WriteLine(result.Output ?? string.Empty);
            return 0;
        }

        var verb = settings.AstAudit ? "audited AST for" : "checked";
        AnsiConsole.MarkupLineInterpolated($"[green]{verb}[/] {Markup.Escape(plan.Value.Name)} ({plan.Value.SourceFiles.Count} source file(s))");
        return 0;
    }
}
