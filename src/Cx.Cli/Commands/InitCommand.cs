using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class InitCommand : Command<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-n|--name <name>")]
        [Description("Project name. Defaults to the current directory name.")]
        public string? Name { get; init; }

        [CommandOption("-f|--force")]
        [Description("Overwrite cx.toml if it already exists.")]
        public bool Force { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var directory = Environment.CurrentDirectory;
        var name = settings.Name ?? new DirectoryInfo(directory).Name;
        return CliServices.CreateProjectScaffold(directory, name, settings.Force);
    }
}
