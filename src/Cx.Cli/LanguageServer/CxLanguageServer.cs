using System.Text;
using System.Text.Json;
using Cx.Compiler;
using Cx.Compiler.Diagnostics;
using Cx.Compiler.Source;

internal sealed class CxLanguageServer(Stream input, Stream output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, OpenDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string? _rootPath;
    private bool _shutdownRequested;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(cancellationToken);
            if (message is null)
            {
                return _shutdownRequested ? 0 : 1;
            }

            using (message)
            {
                if (!message.RootElement.TryGetProperty("method", out var methodElement))
                {
                    continue;
                }

                var method = methodElement.GetString();
                var hasId = message.RootElement.TryGetProperty("id", out var id);
                var parameters = message.RootElement.TryGetProperty("params", out var paramsElement)
                    ? paramsElement
                    : default;

                switch (method)
                {
                    case "initialize":
                        ReadRootPath(parameters);
                        await RespondAsync(id, new
                        {
                            capabilities = new
                            {
                                textDocumentSync = 1,
                            },
                            serverInfo = new { name = "cx-language-server", version = "0.1.0" },
                        }, cancellationToken);
                        break;
                    case "initialized":
                        break;
                    case "textDocument/didOpen":
                        HandleOpenDocument(parameters);
                        await PublishDiagnosticsAsync(cancellationToken);
                        break;
                    case "textDocument/didChange":
                        ChangeDocument(parameters);
                        await PublishDiagnosticsAsync(cancellationToken);
                        break;
                    case "textDocument/didClose":
                        await CloseDocumentAsync(parameters, cancellationToken);
                        await PublishDiagnosticsAsync(cancellationToken);
                        break;
                    case "shutdown":
                        _shutdownRequested = true;
                        await RespondAsync(id, result: null, cancellationToken);
                        break;
                    case "exit":
                        return _shutdownRequested ? 0 : 1;
                    default:
                        if (hasId)
                        {
                            await RespondErrorAsync(id, -32601, $"Method '{method}' is not supported.", cancellationToken);
                        }

                        break;
                }
            }
        }

        return 0;
    }

    private void ReadRootPath(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (parameters.TryGetProperty("rootUri", out var rootUri)
            && rootUri.ValueKind == JsonValueKind.String)
        {
            _rootPath = UriToPath(rootUri.GetString()!);
        }
        else if (parameters.TryGetProperty("rootPath", out var rootPath)
            && rootPath.ValueKind == JsonValueKind.String)
        {
            _rootPath = Path.GetFullPath(rootPath.GetString()!);
        }
    }

    private void HandleOpenDocument(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var uri = document.GetProperty("uri").GetString()!;
        var version = document.TryGetProperty("version", out var versionElement) ? versionElement.GetInt32() : 0;
        _documents[uri] = new OpenDocument(uri, UriToPath(uri), document.GetProperty("text").GetString() ?? string.Empty, version);
    }

    private void ChangeDocument(JsonElement parameters)
    {
        var descriptor = parameters.GetProperty("textDocument");
        var uri = descriptor.GetProperty("uri").GetString()!;
        if (!_documents.TryGetValue(uri, out var document))
        {
            return;
        }

        var changes = parameters.GetProperty("contentChanges");
        if (changes.GetArrayLength() == 0)
        {
            return;
        }

        var text = changes[changes.GetArrayLength() - 1].GetProperty("text").GetString() ?? string.Empty;
        var version = descriptor.TryGetProperty("version", out var versionElement)
            ? versionElement.GetInt32()
            : document.Version + 1;
        _documents[uri] = document with { Text = text, Version = version };
    }

    private async Task CloseDocumentAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        _documents.Remove(uri);
        await NotifyAsync("textDocument/publishDiagnostics", new
        {
            uri,
            diagnostics = Array.Empty<object>(),
        }, cancellationToken);
    }

    private async Task PublishDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_documents.Count == 0)
        {
            return;
        }

        AnalysisResult analysis;
        try
        {
            analysis = new CxCompiler().Analyze(BuildSources());
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"CX language server analysis failed: {exception}");
            return;
        }

        foreach (var document in _documents.Values)
        {
            var diagnostics = analysis.Diagnostics
                .Where(diagnostic => PathsEqual(diagnostic.Location.File.Path, document.Path))
                .Select(diagnostic => ToLspDiagnostic(diagnostic, document.Text))
                .ToArray();
            await NotifyAsync("textDocument/publishDiagnostics", new
            {
                uri = document.Uri,
                version = document.Version,
                diagnostics,
            }, cancellationToken);
        }
    }

    private IReadOnlyList<SourceFile> BuildSources()
    {
        var sources = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        if (_rootPath is not null && Directory.Exists(_rootPath))
        {
            foreach (var source in LoadWorkspaceSources(_rootPath))
            {
                var fullPath = Path.GetFullPath(source.Path);
                sources[fullPath] = source with { Path = fullPath };
            }
        }

        foreach (var document in _documents.Values)
        {
            sources[document.Path] = new SourceFile(document.Path, document.Text);
        }

        return sources.Values.ToList();
    }

    private static IReadOnlyList<SourceFile> LoadWorkspaceSources(string rootPath)
    {
        var configPath = Path.Combine(rootPath, "cx.toml");
        if (File.Exists(configPath))
        {
            var plan = CliServices.ResolveBuildPlan(new BuildPlanRequest(
                InputPath: null,
                ConfigPath: configPath,
                COutputPath: null,
                NativeOutputPath: null,
                Compiler: null,
                CompilerArgs: []));
            if (plan.Success)
            {
                return plan.Value.SourceFiles;
            }
        }

        var conventionalSourceRoot = Path.Combine(rootPath, "src");
        var searchRoot = Directory.Exists(conventionalSourceRoot) ? conventionalSourceRoot : rootPath;
        return Directory.EnumerateFiles(searchRoot, "*.cx", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(path))
            .Select(path => new SourceFile(Path.GetFullPath(path), File.ReadAllText(path)))
            .ToList();
    }

    private static bool IsIgnoredPath(string path)
    {
        var segments = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is ".git" or "bin" or "obj" or "build");
    }

    private static object ToLspDiagnostic(Diagnostic diagnostic, string text)
    {
        var span = diagnostic.EffectiveSpan;
        var start = PositionAt(text, span.Position);
        var end = PositionAt(text, span.End);
        return new
        {
            range = new { start, end },
            severity = diagnostic.Severity == DiagnosticSeverity.Error ? 1 : 2,
            source = "cx",
            message = diagnostic.Message,
        };
    }

    private static object PositionAt(string text, int requestedOffset)
    {
        var offset = Math.Clamp(requestedOffset, 0, text.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        return new { line, character = offset - lineStart };
    }

    private async Task<JsonDocument?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var header = new List<byte>();
        var state = 0;
        while (state < 4)
        {
            var next = await ReadByteAsync(cancellationToken);
            if (next < 0)
            {
                return null;
            }

            header.Add((byte)next);
            state = (state, next) switch
            {
                (0, '\r') => 1,
                (1, '\n') => 2,
                (2, '\r') => 3,
                (3, '\n') => 4,
                (_, '\r') => 1,
                _ => 0,
            };
        }

        var headerText = Encoding.ASCII.GetString(header.ToArray());
        var contentLengthLine = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        if (contentLengthLine is null
            || !int.TryParse(contentLengthLine[(contentLengthLine.IndexOf(':') + 1)..].Trim(), out var contentLength)
            || contentLength < 0)
        {
            throw new InvalidDataException("Missing or invalid Content-Length header.");
        }

        var content = new byte[contentLength];
        await input.ReadExactlyAsync(content, cancellationToken);
        return JsonDocument.Parse(content);
    }

    private async Task<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var read = await input.ReadAsync(buffer, cancellationToken);
        return read == 0 ? -1 : buffer[0];
    }

    private Task RespondAsync(JsonElement id, object? result, CancellationToken cancellationToken) =>
        WriteAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);

    private Task RespondErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken) =>
        WriteAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, cancellationToken);

    private Task NotifyAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteAsync(new { jsonrpc = "2.0", method, @params = parameters }, cancellationToken);

    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {content.Length}\r\n\r\n");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await output.WriteAsync(header, cancellationToken);
            await output.WriteAsync(content, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string UriToPath(string uri) => Path.GetFullPath(new Uri(uri).LocalPath);

    private static bool PathsEqual(string left, string right)
    {
        if (left.StartsWith('<') || right.StartsWith('<'))
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OpenDocument(string Uri, string Path, string Text, int Version);
}
