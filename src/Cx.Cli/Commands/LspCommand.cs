using Spectre.Console.Cli;

internal sealed class LspCommand : Command
{
    protected override int Execute(CommandContext context, CancellationToken cancellationToken) =>
        new CxLanguageServer(Console.OpenStandardInput(), Console.OpenStandardOutput())
            .RunAsync(cancellationToken)
            .GetAwaiter()
            .GetResult();
}
