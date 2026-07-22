using System.Diagnostics;

namespace Cx.Compiler;

internal sealed class CompilationProfiler
{
    private readonly List<CompilationTiming> _timings = [];

    public IReadOnlyList<CompilationTiming> Timings => _timings;

    public T Measure<T>(string name, Func<T> action)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return action();
        }
        finally
        {
            _timings.Add(new CompilationTiming(name, Stopwatch.GetElapsedTime(started)));
        }
    }

    public void Measure(string name, Action action) =>
        Measure(name, () =>
        {
            action();
            return true;
        });
}
