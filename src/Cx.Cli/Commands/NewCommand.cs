using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

internal sealed class NewCommand : Command<NewCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Project directory/name.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("-f|--force")]
        [Description("Overwrite cx.toml if it already exists.")]
        public bool Force { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Name))
        {
            AnsiConsole.MarkupLine("[red]error:[/] Project name is required.");
            return 2;
        }

        var directory = Path.GetFullPath(settings.Name, Environment.CurrentDirectory);
        return CliServices.CreateProjectScaffold(directory, Path.GetFileName(directory), settings.Force);
    }
}
