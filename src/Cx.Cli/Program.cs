using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<TranspileCommand>();
app.Configure(config =>
{
    config.SetApplicationName("cx");
    config.SetApplicationVersion("0.1.0");

    config.AddCommand<TranspileCommand>("transpile")
        .WithDescription("Transpile a CX source file, directory, or configured project to C.");

    config.AddCommand<BuildCommand>("build")
        .WithDescription("Transpile and compile a configured project or input to a native executable.");

    config.AddCommand<RunCommand>("run")
        .WithDescription("Transpile, compile with a C compiler, and run the program.");

    config.AddCommand<CheckCommand>("check")
        .WithDescription("Parse and analyze a project or input without writing generated C.");

    config.AddCommand<LspCommand>("lsp")
        .WithDescription("Run the CX language server over standard input and output.");

    config.AddCommand<TestCommand>("test")
        .WithDescription("Collect CX test blocks, compile a test runner, and run it.");

    config.AddCommand<InitCommand>("init")
        .WithDescription("Create a cx.toml project in the current directory.");

    config.AddCommand<NewCommand>("new")
        .WithDescription("Create a new CX project directory.");

    config.SetExceptionHandler((exception, _) =>
    {
        AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {exception.Message}");
        return 1;
    });
});

return app.Run(RewriteRunProgramArgs(args));

static string[] RewriteRunProgramArgs(string[] args)
{
    if (args.Length == 0 || args[0] != "run")
    {
        return args;
    }

    var separator = Array.IndexOf(args, "--");
    if (separator < 0)
    {
        return args;
    }

    var rewritten = args[..separator].ToList();
    foreach (var programArg in args[(separator + 1)..])
    {
        rewritten.Add("--program-arg");
        rewritten.Add(programArg);
    }

    return rewritten.ToArray();
}
