namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using System.Diagnostics;
using System.Linq;

/// <summary>
/// Times the named steps of one generated method's emission (lift, IR passes, lowering and
/// structuring, printing) so a method that takes long to generate can be reported with its
/// per-step breakdown.
/// </summary>
internal sealed class GenerationStepTimer {
    private readonly List<(string Step, long Milliseconds)> _steps = [];

    /// <summary>The measured steps, in execution order.</summary>
    public IReadOnlyList<(string Step, long Milliseconds)> Steps => _steps;

    /// <summary>The sum of the measured steps.</summary>
    public long TotalMilliseconds => _steps.Sum(step => step.Milliseconds);

    /// <summary>Times <paramref name="action"/> and records it under <paramref name="step"/>.</summary>
    public void Measure(string step, Action action) {
        long start = Stopwatch.GetTimestamp();
        action();
        Record(step, ElapsedMilliseconds(start));
    }

    /// <summary>Times <paramref name="action"/>, records it under <paramref name="step"/> and returns its value.</summary>
    public T Measure<T>(string step, Func<T> action) {
        long start = Stopwatch.GetTimestamp();
        T result = action();
        Record(step, ElapsedMilliseconds(start));
        return result;
    }

    private static long ElapsedMilliseconds(long start) => (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>Records <paramref name="milliseconds"/> under <paramref name="step"/>.</summary>
    internal void Record(string step, long milliseconds) => _steps.Add((step, milliseconds));
}
