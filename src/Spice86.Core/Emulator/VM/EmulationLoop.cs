namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// This class orchestrates the execution of the emulated CPU, <br/>
/// throttles CPU speed for the rare speed sensitive games, <br/>
/// checks breakpoints each cycle, triggers PIT ticks, and ensures DMA transfers are performed
///.</summary>
public class EmulationLoop : ICyclesLimiter {
    private readonly ILoggerService _loggerService;
    private readonly IInstructionExecutor _cpu;
    private readonly FunctionHandler _functionHandler;
    private readonly State _cpuState;
    private readonly Timer _timer;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly PerformanceMeasurer _performanceMeasurer = new();
    private readonly Stopwatch _performanceStopwatch = new();
    private readonly Stopwatch _highPrecisionSleepStopwatch = new();
    private readonly DmaController _dmaController;

    // Signed error accumulator: positive means we're ahead (should slow down), negative means behind.
    private long _cycleErrorAccumulator;
    private long _cyclesAtLastCheck;
    private long _lastCheckTicks;
    private long _nextCycleCheckThreshold;
    private double _adjustWindowMs; // dynamic gate window
    private readonly double _ticksToMsFactor = 1000.0 / Stopwatch.Frequency;
    private int _lastTargetCpuCyclesPerMs;
    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    public IPerformanceMeasureReader CpuPerformanceMeasurer => _performanceMeasurer;

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }
    public int TargetCpuCyclesPerMs { get; set; } = ICyclesLimiter.RealModeCpuCylcesPerMs;

    private const int MinAdjustWindowMs = 4; // min window to reduce jitter
    private const int MaxAdjustWindowMs = 16; // larger window at high targets to reduce QPC calls

    // Above this target, limiter is fully disabled and avoids any Stopwatch calls.
    private const int DisableLimiterThreshold = 500_000;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="configuration">The emulator configuration. This is what to run and how.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="dmaController">The Direct Memory Access controller chip.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    public EmulationLoop(Configuration configuration,
        FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState,
        Timer timer, EmulatorBreakpointsManager emulatorBreakpointsManager,
        DmaController dmaController,
        IPauseHandler pauseHandler, ILoggerService loggerService) {
        _loggerService = loggerService;
        _dmaController = dmaController;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _timer = timer;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        if (configuration.Cycles != null) {
            long cfg = configuration.Cycles.Value;
            if (cfg <= 0) {
                TargetCpuCyclesPerMs = ICyclesLimiter.RealModeCpuCylcesPerMs;
            } else {
                TargetCpuCyclesPerMs = (int)Math.Clamp(cfg, MinCyclesPerMs, MaxCyclesPerMs);
            }
        }

        if (TargetCpuCyclesPerMs <= 0) {
            TargetCpuCyclesPerMs = ICyclesLimiter.RealModeCpuCylcesPerMs;
        }
    }

    /// <summary>
    /// Starts and waits for the end of the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. This can occur if the target program is not supported (yet).</exception>
    public void Run() {
        try {
            StartRunLoop(_functionHandler);
        } catch (HaltRequestedException) {
            // Actually a signal generated code requested Exit
            return;
        } catch (InvalidVMOperationException) {
            throw;
        } catch (Exception e) {
            throw new InvalidVMOperationException(_cpuState, e);
        }
        _emulatorBreakpointsManager.OnMachineStop();
        _cpu.SignalEnd();
    }

    /// <summary>
    /// Forces the emulation loop to exit.
    /// </summary>
    internal void Exit() {
        _cpuState.IsRunning = false;
        IsPaused = false;
    }

    private void StartRunLoop(FunctionHandler functionHandler) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, _cpuState.IpSegmentedAddress, null, null, "entry", false);
        RunLoop();
    }

    private void RunLoop() {
        _performanceStopwatch.Start();
        _cyclesAtLastCheck = _cpuState.Cycles;
        _lastCheckTicks = _performanceStopwatch.ElapsedTicks;
        _lastTargetCpuCyclesPerMs = TargetCpuCyclesPerMs;
        _adjustWindowMs = ComputeAdjustWindowMs(_lastTargetCpuCyclesPerMs);
        // If disabled, avoid any future timing checks by parking the threshold.
        _nextCycleCheckThreshold = _lastTargetCpuCyclesPerMs >= DisableLimiterThreshold
            ? long.MaxValue
            : _cyclesAtLastCheck + (long)(_lastTargetCpuCyclesPerMs * _adjustWindowMs);

        _cpu.SignalEntry();
        while (_cpuState.IsRunning) {
            RunOnce();
        }
        _performanceStopwatch.Stop();
        OutputPerfStats();
    }

    private void RunOnce() {
        _emulatorBreakpointsManager.CheckExecutionBreakPoints();
        _pauseHandler.WaitIfPaused();
        _cpu.ExecuteNext();
        _performanceMeasurer.UpdateValue(_cpuState.Cycles);
        _timer.Tick();
        _dmaController.PerformDmaTransfers();
        AdjustCycles();
    }

    private void AdjustCycles() {
        // Fast path 1: fully disabled limiter — absolutely no QPC calls.
        if (_nextCycleCheckThreshold == long.MaxValue) {
            // If target changed downwards later, next block will reconfigure.
            if (TargetCpuCyclesPerMs == _lastTargetCpuCyclesPerMs) {
                return;
            }
        }

        long currentCycles = _cpuState.Cycles;

        // Fast path 2: target changed — reconfigure thresholds with zero timestamp calls.
        int target = TargetCpuCyclesPerMs;
        if (target != _lastTargetCpuCyclesPerMs) {
            _lastTargetCpuCyclesPerMs = target;
            _adjustWindowMs = ComputeAdjustWindowMs(target);
            if (target >= DisableLimiterThreshold) {
                _cycleErrorAccumulator = 0;
                _nextCycleCheckThreshold = long.MaxValue; // fully disable
                return;
            }

            _cyclesAtLastCheck = currentCycles;
            _lastCheckTicks = _performanceStopwatch.ElapsedTicks; // single read on re-enable
            _nextCycleCheckThreshold = currentCycles + (long)(target * _adjustWindowMs);
            return;
        }

        // Fast path 3: not yet time to check — zero-cost.
        if (currentCycles < _nextCycleCheckThreshold) {
            return;
        }

        long nowTicks = _performanceStopwatch.ElapsedTicks;
        long elapsedTicks = nowTicks - _lastCheckTicks;
        if (elapsedTicks <= 0) {
            _lastCheckTicks = nowTicks;
            _cyclesAtLastCheck = currentCycles;
            _nextCycleCheckThreshold = currentCycles + (long)(_lastTargetCpuCyclesPerMs * _adjustWindowMs);
            return;
        }

        // Window already adjusted on target change; no need to recompute here.
        double elapsedMs = elapsedTicks * _ticksToMsFactor;
        long cyclesExecuted = currentCycles - _cyclesAtLastCheck;
        double targetCyclesForPeriod = _lastTargetCpuCyclesPerMs * elapsedMs;

        long cyclesDifference = (long)Math.Round(cyclesExecuted - targetCyclesForPeriod);

        // Signed accumulator: positive -> ahead (need to sleep), negative -> behind (carry debt)
        _cycleErrorAccumulator += cyclesDifference;
        if (_cycleErrorAccumulator > 0) {
            double msToSleep = _cycleErrorAccumulator / (double)_lastTargetCpuCyclesPerMs;
            DoHybridSleep(msToSleep);
            // After sleeping, assume consumed the positive error (best effort)
            _cycleErrorAccumulator = 0;
        }

        _cyclesAtLastCheck = currentCycles;
        _lastCheckTicks = nowTicks;
        // Schedule next check based on cycles to avoid extra QPC calls in the hot path
        _nextCycleCheckThreshold = currentCycles + (long)(_lastTargetCpuCyclesPerMs * _adjustWindowMs);
    }

    private static double ComputeAdjustWindowMs(int targetCyclesPerMs) {
        return targetCyclesPerMs switch {
            // Larger windows for high targets reduce frequency of QPC calls while keeping control responsiveness
            >= 50_000 => MaxAdjustWindowMs,
            >= 20_000 => 8.0,
            _ => MinAdjustWindowMs
        };
    }


    private void DoHybridSleep(double msToSleep) {
        if (msToSleep <= 0) {
            return;
        }

        _highPrecisionSleepStopwatch.Restart();
        long targetTicks = (long)(msToSleep * Stopwatch.Frequency / 1000.0);
        var spinner = new SpinWait();
        while (_cpuState.IsRunning && _highPrecisionSleepStopwatch.ElapsedTicks < targetTicks) {
            spinner.SpinOnce();
        }

    }

    internal void RunFromUntil(SegmentedAddress startAddress, SegmentedAddress endAddress) {
        _cpuState.IpSegmentedAddress = startAddress;
        while (_cpuState.IsRunning && _cpuState.IpSegmentedAddress != endAddress) {
            RunOnce();
        }
    }

    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            long cyclesPerSeconds = _performanceMeasurer.AverageValuePerSecond;
            long elapsedTimeInMilliseconds = _performanceStopwatch.ElapsedMilliseconds;
            _loggerService.Warning(
                "Executed {Cycles} cycles in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} cycles per second on average over run.",
                _cpuState.Cycles, elapsedTimeInMilliseconds, cyclesPerSeconds);
        }
    }

    public void IncreaseCycles() {
        // Percentage step with floor to keep low values usable
        int step = Math.Max(CyclesUp, (int)(TargetCpuCyclesPerMs * 0.10));
        TargetCpuCyclesPerMs = Math.Min(TargetCpuCyclesPerMs + step, MaxCyclesPerMs);
    }

    public void DecreaseCycles() {
        int step = Math.Max(CyclesDown, (int)(TargetCpuCyclesPerMs * 0.10));
        TargetCpuCyclesPerMs = Math.Max(TargetCpuCyclesPerMs - step, MinCyclesPerMs);
    }
}
