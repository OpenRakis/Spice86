// Debug-mode performance test - bypasses BenchmarkDotNet to run in Debug builds
// Run with: dotnet run -c Debug -- debug-perf

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;

namespace Spice86.MicroBenchmarkTemplate;

public static class DebugModePerformanceTest
{
    private const int TotalCycles = 1_000_000;
    private const int WarmupCycles = 100_000;
    private const int Iterations = 1000;

    public static void Run()
    {
        Console.WriteLine("=== Emulation Loop Performance Test ===");
        Console.WriteLine($"Build: {(IsDebugBuild() ? "DEBUG" : "RELEASE")}");
        Console.WriteLine($"Cycles per iteration: {TotalCycles:N0}");
        Console.WriteLine();

        var results = new List<(string Name, double DebugNs, double ReleaseNs)>();

        // Section 1: Basic Loop Operations
        Console.WriteLine("--- Basic Loop Operations ---");
        RunTest("Bare loop (baseline)", TestBareLoop);
        RunTest("State.AdvanceCycles", TestAdvanceCycles);
        RunTest("State.IncCycles", TestIncCycles);

        // Section 2: Cycles Limiter
        Console.WriteLine("\n--- Cycles Limiter ---");
        RunTest("RegulateCycles fast path (60k c/ms)", TestRegulateCyclesFastPath);
        RunTest("RegulateCycles with ticks (3k c/ms, no wait)", TestRegulateCyclesWithTicks);
        RunTest("GetCycleProgressionPercentage", TestGetCycleProgression);
        RunTest("TickOccurred check", TestTickOccurredCheck);

        // Section 3: Clock Operations
        Console.WriteLine("\n--- Clock Operations ---");
        RunTest("Clock.FullIndex", TestClockFullIndex);
        RunTest("Clock.ConvertTimeToCycles", TestConvertTimeToCycles);

        // Section 4: Memory Operations (simulated)
        Console.WriteLine("\n--- Memory Operations (simulated) ---");
        RunTest("Array access (byte[])", TestArrayAccess);
        RunTest("Span<byte> access", TestSpanAccess);
        RunTest("Dictionary lookup", TestDictionaryLookup);
        RunTest("ConcurrentDictionary lookup", TestConcurrentDictLookup);

        // Section 5: Virtual/Interface Dispatch
        Console.WriteLine("\n--- Virtual/Interface Dispatch ---");
        RunTest("Direct method call", TestDirectCall);
        RunTest("Virtual method call", TestVirtualCall);
        RunTest("Interface call", TestInterfaceCall);
        RunTest("Delegate invoke", TestDelegateInvoke);

        // Section 6: Threading Primitives
        Console.WriteLine("\n--- Threading Primitives ---");
        RunTest("Lock acquire/release", TestLockAcquire);
        RunTest("Volatile.Read (long)", TestVolatileRead);
        RunTest("Volatile.Write (double)", TestVolatileWrite);
        RunTest("Interlocked.Increment", TestInterlockedIncrement);

        // Section 7: Simulated Emulation Loop
        Console.WriteLine("\n--- Simulated Emulation Loop ---");
        RunTest("Minimal loop (AdvanceCycles only)", TestMinimalLoop);
        RunTest("Basic loop (+ RegulateCycles fast)", TestBasicLoop);
        RunTest("Full loop (+ ticks, scheduler sim)", TestFullLoop);
        RunTest("Full loop + breakpoint check", TestFullLoopWithBreakpoints);

        // Summary
        PrintSummary();
    }

    private static void RunTest(string name, Func<(double nsPerOp, string extra)> test)
    {
        // Warmup
        test();

        var sw = Stopwatch.StartNew();
        var result = test();
        sw.Stop();

        string extra = string.IsNullOrEmpty(result.extra) ? "" : $" [{result.extra}]";
        Console.WriteLine($"  {name,-45} {result.nsPerOp,8:F2} ns/op{extra}");
    }

    #region Basic Loop Operations

    private static (double, string) TestBareLoop()
    {
        long counter = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            counter++;
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"counter={counter}");
    }

    private static (double, string) TestAdvanceCycles()
    {
        var state = new State(CpuModel.INTEL_80386);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.AdvanceCycles(1);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestIncCycles()
    {
        var state = new State(CpuModel.INTEL_80386);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.IncCycles();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    #endregion

    #region Cycles Limiter

    private static (double, string) TestRegulateCyclesFastPath()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, 60000); // High cycles = few tick boundaries
        limiter.OnResume();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.AdvanceCycles(1);
            limiter.RegulateCycles();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"ticks={limiter.TickCount}");
    }

    private static (double, string) TestRegulateCyclesWithTicks()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        limiter.OnResume();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.AdvanceCycles(1);
            limiter.RegulateCycles();
        }
        sw.Stop();
        double perTickUs = sw.Elapsed.TotalMicroseconds / limiter.TickCount;
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"ticks={limiter.TickCount}, {perTickUs:F1}µs/tick");
    }

    private static (double, string) TestGetCycleProgression()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        limiter.OnResume();
        double sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += limiter.GetCycleProgressionPercentage();
            state.AdvanceCycles(1);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestTickOccurredCheck()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        limiter.OnResume();
        int count = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            if (limiter.TickOccurred) count++;
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"count={count}");
    }

    #endregion

    #region Clock Operations

    private static (double, string) TestClockFullIndex()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        var clock = new EmulatedClock(limiter);
        limiter.OnResume();
        double sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += clock.FullIndex;
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestConvertTimeToCycles()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        var clock = new EmulatedClock(limiter);
        limiter.OnResume();
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += clock.ConvertTimeToCycles(1.5);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    #endregion

    #region Memory Operations

    private static (double, string) TestArrayAccess()
    {
        var array = new byte[1024 * 1024]; // 1MB
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += array[i & 0xFFFFF]; // Wrap within array
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestSpanAccess()
    {
        var array = new byte[1024 * 1024];
        Span<byte> span = array;
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += span[i & 0xFFFFF];
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestDictionaryLookup()
    {
        var dict = new Dictionary<int, int>();
        for (int i = 0; i < 1000; i++) dict[i] = i;
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            if (dict.TryGetValue(i % 1000, out int val))
                sum += val;
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestConcurrentDictLookup()
    {
        var dict = new ConcurrentDictionary<int, int>();
        for (int i = 0; i < 1000; i++) dict[i] = i;
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            if (dict.TryGetValue(i % 1000, out int val))
                sum += val;
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    #endregion

    #region Virtual/Interface Dispatch

    private sealed class ConcreteClass
    {
        public int Value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetValue() => Value++;
    }

    private class BaseClass
    {
        public int Value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual int GetValue() => Value++;
    }

    private interface ICounter
    {
        int GetValue();
    }

    private sealed class InterfaceImpl : ICounter
    {
        public int Value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetValue() => Value++;
    }

    private static (double, string) TestDirectCall()
    {
        var obj = new ConcreteClass();
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += obj.GetValue();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestVirtualCall()
    {
        BaseClass obj = new BaseClass();
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += obj.GetValue();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestInterfaceCall()
    {
        ICounter obj = new InterfaceImpl();
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += obj.GetValue();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestDelegateInvoke()
    {
        int value = 0;
        Func<int> del = () => value++;
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += del();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    #endregion

    #region Threading Primitives

    private static (double, string) TestLockAcquire()
    {
        var lockObj = new object();
        long counter = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            lock (lockObj)
            {
                counter++;
            }
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestVolatileRead()
    {
        long value = 42;
        long sum = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            sum += Volatile.Read(ref value);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestVolatileWrite()
    {
        double value = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            Volatile.Write(ref value, i * 0.001);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestInterlockedIncrement()
    {
        long value = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            Interlocked.Increment(ref value);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    #endregion

    #region Simulated Emulation Loop

    private static (double, string) TestMinimalLoop()
    {
        var state = new State(CpuModel.INTEL_80386);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            // Minimal: just advance cycles
            state.AdvanceCycles(1);
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, "");
    }

    private static (double, string) TestBasicLoop()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new CpuCycleLimiter(state, 60000);
        limiter.OnResume();

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.AdvanceCycles(1);
            limiter.RegulateCycles();
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"ticks={limiter.TickCount}");
    }

    private static (double, string) TestFullLoop()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        limiter.OnResume();
        int tickHandlerCalls = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            state.AdvanceCycles(1);
            limiter.RegulateCycles();
            if (limiter.TickOccurred)
            {
                // Simulate scheduler work
                tickHandlerCalls++;
            }
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"ticks={limiter.TickCount}, handlers={tickHandlerCalls}");
    }

    private static (double, string) TestFullLoopWithBreakpoints()
    {
        var state = new State(CpuModel.INTEL_80386);
        var limiter = new NoWaitCycleLimiter(state, 3000);
        limiter.OnResume();
        bool hasBreakpoints = false; // Simulate HasActiveBreakpoints check
        int tickHandlerCalls = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < TotalCycles; i++)
        {
            // Breakpoint check (fast path when none)
            if (hasBreakpoints)
            {
                // Would check breakpoints here
            }

            state.AdvanceCycles(1);
            limiter.RegulateCycles();
            if (limiter.TickOccurred)
            {
                tickHandlerCalls++;
            }
        }
        sw.Stop();
        return (sw.Elapsed.TotalNanoseconds / TotalCycles, $"ticks={limiter.TickCount}");
    }

    #endregion

    private static void PrintSummary()
    {
        Console.WriteLine("\n=== Performance Summary ===");
        Console.WriteLine("Target for 10000 cycles/ms: <100 ns/instruction total overhead");
        Console.WriteLine("Target for 3000 cycles/ms:  <333 ns/instruction total overhead");
        Console.WriteLine();
        Console.WriteLine("Key ratios to watch (Debug vs Release):");
        Console.WriteLine("  - Virtual/Interface calls: should be <3x slower in Debug");
        Console.WriteLine("  - Lock operations: should be similar in Debug/Release");
        Console.WriteLine("  - Full loop: should be <5x slower in Debug");
        Console.WriteLine();
        Console.WriteLine("If Debug is >10x slower than Release, look for:");
        Console.WriteLine("  - Missing [MethodImpl(AggressiveInlining)] on hot paths");
        Console.WriteLine("  - Excessive interface dispatch in inner loops");
        Console.WriteLine("  - LINQ or closures allocating in hot paths");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// A version of CpuCycleLimiter that doesn't wait - for measuring pure overhead
    /// </summary>
    private class NoWaitCycleLimiter : ICyclesLimiter
    {
        private readonly State _state;
        private long _targetCyclesForPause;
        private uint _tickCount;
        private int _tickCycleMax;
        private double _inverseTickCycleMax;
        private double _atomicFullIndex;
        private long _ioDelayRemoved;
        private bool _isRunning;

        public NoWaitCycleLimiter(State state, int targetCpuCyclesPerMs)
        {
            _state = state;
            TargetCpuCyclesPerMs = targetCpuCyclesPerMs;
            _tickCycleMax = targetCpuCyclesPerMs;
            _inverseTickCycleMax = 1.0 / _tickCycleMax;
            _targetCyclesForPause = _tickCycleMax;
        }

        public bool TickOccurred { get; private set; }
        public uint TickCount => _tickCount;
        public int TickCycleMax => _tickCycleMax;
        public long NextTickBoundaryCycles => _targetCyclesForPause;
        public int TargetCpuCyclesPerMs { get; set; }
        public double AtomicFullIndex => Volatile.Read(ref _atomicFullIndex);
        public long IoDelayRemoved => _ioDelayRemoved;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RegulateCycles()
        {
            TickOccurred = false;
            if (_state.Cycles < _targetCyclesForPause)
            {
                return;
            }
            RegulateCyclesSlowPath();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegulateCyclesSlowPath()
        {
            if (!_isRunning) return;

            TickOccurred = true;
            _tickCount++;
            _ioDelayRemoved = 0;
            _tickCycleMax = TargetCpuCyclesPerMs;
            _inverseTickCycleMax = 1.0 / _tickCycleMax;
            _targetCyclesForPause = _state.Cycles + _tickCycleMax;

            // NO WAITING - this is the key difference
            // Just update atomic index
            UpdateAtomicIndex();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetCycleProgressionPercentage()
        {
            long tickStart = _targetCyclesForPause - _tickCycleMax;
            long cyclesDone = _state.Cycles - tickStart;
            double fraction = cyclesDone * _inverseTickCycleMax;
            return Math.Max(fraction, 0.0);
        }

        private void UpdateAtomicIndex()
        {
            double fullIndex = _tickCount + GetCycleProgressionPercentage();
            Volatile.Write(ref _atomicFullIndex, fullIndex);
        }

        public void OnPause() => _isRunning = false;
        public void OnResume()
        {
            _isRunning = true;
            _targetCyclesForPause = _state.Cycles + _tickCycleMax;
        }

        public long GetNumberOfCyclesNotDoneYet()
        {
            long tickStart = _targetCyclesForPause - _tickCycleMax;
            return _state.Cycles - tickStart;
        }

        public void ConsumeIoCycles(int cycles)
        {
            long remaining = _targetCyclesForPause - _state.Cycles;
            int clamped = (int)Math.Min(cycles, remaining);
            if (clamped > 0)
            {
                _state.AdvanceCycles(clamped);
                _ioDelayRemoved += clamped;
            }
        }

        public void IncreaseCycles() => TargetCpuCyclesPerMs += 100;
        public void DecreaseCycles() => TargetCpuCyclesPerMs = Math.Max(100, TargetCpuCyclesPerMs - 100);
    }
}
