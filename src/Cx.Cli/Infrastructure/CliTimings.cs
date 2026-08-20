using System.Diagnostics;
using System.Globalization;
using Cx.Compiler;

internal sealed class CliTimings(bool enabled, TextWriter? writer = null) : IDisposable
{
    private readonly TextWriter _writer = writer ?? Console.Error;
    private readonly long _commandStarted = Stopwatch.GetTimestamp();
    private readonly List<(string Name, TimeSpan Duration)> _additional = [];
    private TimeSpan _projectResolution;
    private CompilationResult? _compilation;
    private TimeSpan? _compilerTotal;
    private bool _written;

    public void RecordProjectResolution(TimeSpan duration)
    {
        if (enabled)
        {
            _projectResolution = duration;
        }
    }

    public void RecordCompilation(CompilationResult result, TimeSpan duration)
    {
        if (enabled)
        {
            _compilation = result;
            _compilerTotal = duration;
        }
    }

    public void Record(string name, TimeSpan duration)
    {
        if (enabled)
        {
            _additional.Add((name, duration));
        }
    }

    public void Dispose() =>
        Write(Stopwatch.GetElapsedTime(_commandStarted));

    internal void Write(TimeSpan commandTotal)
    {
        if (!enabled || _written)
        {
            return;
        }

        _written = true;
        _writer.WriteLine("timings:");
        WriteTiming("Project resolution", _projectResolution, indent: 1);
        if (_compilation is not null)
        {
            foreach (var timing in _compilation.Timings)
            {
                WriteTiming(timing.Name, timing.Duration, indent: 2);
            }
        }
        if (_compilerTotal is not null)
        {
            WriteTiming("Compiler total", _compilerTotal.Value, indent: 1);
        }
        foreach (var timing in _additional)
        {
            WriteTiming(timing.Name, timing.Duration, indent: 1);
        }
        WriteTiming("Command total", commandTotal, indent: 0);
    }

    private void WriteTiming(string name, TimeSpan duration, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var milliseconds = duration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture);
        _writer.WriteLine($"{prefix}{name,-39} {milliseconds,10} ms");
    }
}
