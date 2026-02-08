namespace Spice86.MicroBenchmarkTemplate;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

using Spice86.Shared.Utils;

using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Benchmark for HighResolutionWaiter and timing primitives.
/// Critical for maintaining accurate emulation speed without burning CPU.
/// DOSBox reference: increase_ticks() sleep/spin logic.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class HighResolutionWaiterBenchmark {
    private Stopwatch _stopwatch = null!;
    private static readonly long TicksPerMs = Stopwatch.Frequency / 1000;
    
    private const int Iterations = 1000;

    [GlobalSetup]
    public void Setup() {
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Baseline: Stopwatch.ElapsedTicks access cost.
    /// This is called in the hot path for timing checks.
    /// </summary>
    [Benchmark(Baseline = true)]
    public long StopwatchElapsedTicks() {
        long total = 0;
        for (int i = 0; i < Iterations * 100; i++) {
            total += _stopwatch.ElapsedTicks;
        }
        return total;
    }

    /// <summary>
    /// Stopwatch.Frequency division cost (for ms conversion).
    /// </summary>
    [Benchmark]
    public double TicksToMilliseconds() {
        double total = 0;
        for (int i = 0; i < Iterations * 100; i++) {
            long ticks = _stopwatch.ElapsedTicks;
            total += ticks * 1000.0 / Stopwatch.Frequency;
        }
        return total;
    }

    /// <summary>
    /// Pre-computed ticks per ms (like DOSBox does).
    /// </summary>
    [Benchmark]
    public double PrecomputedTicksPerMs() {
        double total = 0;
        long ticksPerMs = TicksPerMs;
        for (int i = 0; i < Iterations * 100; i++) {
            long ticks = _stopwatch.ElapsedTicks;
            total += (double)ticks / ticksPerMs;
        }
        return total;
    }

    /// <summary>
    /// SpinWait.SpinOnce() cost - used in sub-ms waits.
    /// </summary>
    [Benchmark]
    public void SpinWaitCost() {
        var spinner = new SpinWait();
        for (int i = 0; i < Iterations; i++) {
            spinner.SpinOnce();
            spinner.Reset();
        }
    }

    /// <summary>
    /// Thread.Yield() cost - used in 0.05-1ms range.
    /// </summary>
    [Benchmark]
    public void ThreadYieldCost() {
        for (int i = 0; i < 100; i++) {
            Thread.Yield();
        }
    }

    /// <summary>
    /// ManualResetEventSlim.Wait cost - used for >= 1ms.
    /// </summary>
    [Benchmark]
    public void ManualResetEventWait() {
        var handle = new ManualResetEventSlim(false);
        for (int i = 0; i < 10; i++) {
            handle.Wait(TimeSpan.FromTicks(1)); // Minimal wait
        }
        handle.Dispose();
    }

    /// <summary>
    /// Full WaitUntil call with very short target (spin path).
    /// </summary>
    [Benchmark]
    public bool WaitUntil_SpinPath() {
        long target = _stopwatch.ElapsedTicks + 100; // 100 ticks ahead (~10us)
        return HighResolutionWaiter.WaitUntil(_stopwatch, target);
    }

    /// <summary>
    /// WaitUntil with target already passed (immediate return).
    /// </summary>
    [Benchmark]
    public bool WaitUntil_AlreadyPassed() {
        long target = _stopwatch.ElapsedTicks - 1;
        bool result = false;
        for (int i = 0; i < Iterations; i++) {
            result = HighResolutionWaiter.WaitUntil(_stopwatch, target);
        }
        return result;
    }

    /// <summary>
    /// Compares long comparison patterns (hot path optimization).
    /// </summary>
    [Benchmark]
    public int LongComparisonPatterns() {
        long a = 1000000;
        long b = 1000001;
        int count = 0;
        for (int i = 0; i < Iterations * 100; i++) {
            if (a < b) count++;
            a++;
        }
        return count;
    }
}

/// <summary>
/// Benchmark for volatile and atomic operations used in timing.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class AtomicOperationsBenchmark {
    private double _volatileDouble;
    private long _volatileLong;
    
    private const int Iterations = 10_000_000;

    [GlobalSetup]
    public void Setup() {
        _volatileDouble = 0.0;
        _volatileLong = 0;
    }

    [Benchmark(Baseline = true)]
    public int RegularIntIncrement() {
        int value = 0;
        for (int i = 0; i < Iterations; i++) {
            value++;
        }
        return value;
    }

    [Benchmark]
    public long VolatileReadLong() {
        long total = 0;
        for (int i = 0; i < Iterations; i++) {
            total += Volatile.Read(ref _volatileLong);
        }
        return total;
    }

    [Benchmark]
    public void VolatileWriteLong() {
        for (int i = 0; i < Iterations; i++) {
            Volatile.Write(ref _volatileLong, i);
        }
    }

    [Benchmark]
    public double VolatileReadDouble() {
        double total = 0;
        for (int i = 0; i < Iterations; i++) {
            total += Volatile.Read(ref _volatileDouble);
        }
        return total;
    }

    [Benchmark]
    public void VolatileWriteDouble() {
        for (int i = 0; i < Iterations; i++) {
            Volatile.Write(ref _volatileDouble, i);
        }
    }

    [Benchmark]
    public long InterlockedIncrement() {
        long value = 0;
        for (int i = 0; i < Iterations; i++) {
            Interlocked.Increment(ref value);
        }
        return value;
    }

    [Benchmark]
    public int InterlockedCompareExchange() {
        int value = 0;
        for (int i = 0; i < Iterations; i++) {
            Interlocked.CompareExchange(ref value, i, i - 1);
        }
        return value;
    }
}

/// <summary>
/// Benchmark for inline vs non-inline method patterns.
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class InliningBenchmark {
    private long _cycles;
    private long _threshold;
    
    private const int Iterations = 10_000_000;

    [GlobalSetup]
    public void Setup() {
        _cycles = 0;
        _threshold = Iterations / 2;
    }

    [Benchmark(Baseline = true)]
    public int DirectInlineCheck() {
        int count = 0;
        long cycles = 0;
        long threshold = Iterations / 2;
        for (int i = 0; i < Iterations; i++) {
            cycles++;
            if (cycles < threshold) {
                count++;
            }
        }
        return count;
    }

    [Benchmark]
    public int MethodCallCheck() {
        int count = 0;
        _cycles = 0;
        for (int i = 0; i < Iterations; i++) {
            _cycles++;
            if (CheckThreshold()) {
                count++;
            }
        }
        return count;
    }

    [Benchmark]
    public int InlinedMethodCheck() {
        int count = 0;
        _cycles = 0;
        for (int i = 0; i < Iterations; i++) {
            _cycles++;
            if (CheckThresholdInlined()) {
                count++;
            }
        }
        return count;
    }

    private bool CheckThreshold() {
        return _cycles < _threshold;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CheckThresholdInlined() {
        return _cycles < _threshold;
    }
}
