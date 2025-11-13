namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.CycleBudget;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Utils;

using System.Diagnostics;

/// <summary>
/// Coordinates the execution of the emulated CPU, enforces timing limits, checks breakpoints,
/// triggers hardware timers, and keeps DMA transfers moving forward.
/// </summary>
public class EmulationLoop : ICyclesLimiter {
    private readonly ILoggerService _loggerService;
    private readonly IInstructionExecutor _cpu;
    private readonly FunctionHandler _functionHandler;
    private readonly State _cpuState;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;

    private readonly PerformanceMeasurer _performanceMeasurer = new(new PerformanceMeasureOptions {
        CheckInterval = 512,
        MinValueDelta = 3000,
        MaxIntervalMilliseconds = 10
    });
    private readonly Stopwatch _performanceStopwatch = new();
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly DualPic _dualPic;
    private readonly ExecutionStateSlice _executionStateSlice;
    private readonly Stopwatch _sliceStopwatch = new();
    private long _nextSliceTick;
    private bool _sliceInitialized;
    private readonly long _sliceDurationTicks;
    private readonly ICyclesBudgeter _cyclesBudgeter;

    /// <summary>
    ///     Gets a reader exposing CPU performance metrics.
    /// </summary>
    public IPerformanceMeasureReader CpuPerformanceMeasurer => _performanceMeasurer;

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    ///     Gets or sets the target number of CPU cycles the emulator may execute per millisecond.
    /// </summary>
    public int TargetCpuCyclesPerMs {
        get => _cyclesLimiter.TargetCpuCyclesPerMs;
        set => _cyclesLimiter.TargetCpuCyclesPerMs = value;
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    /// <param name="executionStateSlice">Shared cycle budgeting state consumed by the Device Scheduler or PIT scheduler.</param>
    /// <param name="dualPic">Programmable interrupt controller driving hardware IRQ delivery.</param>
    /// <param name="cyclesLimiter">Limits the number of executed instructions per slice</param>
    /// <param name="cyclesBudgeter">Budgets how many cycles are available per slice</param>
    public EmulationLoop(FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState,
        ExecutionStateSlice executionStateSlice, DualPic dualPic,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, ILoggerService loggerService, ICyclesLimiter cyclesLimiter, ICyclesBudgeter cyclesBudgeter) {
        _loggerService = loggerService;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _executionStateSlice = executionStateSlice;
        _dualPic = dualPic;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _cyclesLimiter = cyclesLimiter;
        _sliceDurationTicks = Math.Max(1, Stopwatch.Frequency / 1000);
        _cyclesBudgeter = cyclesBudgeter;
        _pauseHandler.Paused += OnPauseStateChanged;
        _pauseHandler.Resumed += OnPauseStateChanged;
        _sliceStopwatch.Start();
    }

    /// <summary>
    /// Starts and waits for the end of the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">
    /// Thrown when an unhandled exception occurs, typically because the target program is not supported yet.
    /// </exception>
    public void Run() {
        _emulatorBreakpointsManager.OnMachineStart();
        try {
            StartRunLoop(_functionHandler);
        } catch (HaltRequestedException) {
            // Actually a signal-generated code requested Exit
            _loggerService.Information("Emulation halted by request.");
            return;
        } catch (InvalidVMOperationException invalidVmOperationException) {
            _loggerService.Error(invalidVmOperationException,
                "Emulation halted because of an invalid virtual machine operation.");
            throw;
        } catch (Exception e) {
            _loggerService.Error(e, "Emulation failed with unhandled exception.");
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

    /// <summary>
    ///     Invokes the entry point managed by the <see cref="FunctionHandler" /> and executes the run loop.
    /// </summary>
    /// <param name="functionHandler">Handler used to call into emulated machine code.</param>
    private void StartRunLoop(FunctionHandler functionHandler) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, _cpuState.IpSegmentedAddress, null, null, "entry", false);
        RunLoop();
        functionHandler.Ret(CallType.MACHINE, null);
    }

    /// <summary>
    ///     Executes the main emulation loop until the emulated CPU stops running.
    /// </summary>
    private void RunLoop() {
        _performanceStopwatch.Start();
        _cpu.SignalEntry();
        ResetSliceTimer();
        while (_cpuState.IsRunning) {
            bool runImmediately;
            do {
                runImmediately = RunSlice();
            } while (runImmediately && _cpuState.IsRunning);
        }
        _performanceStopwatch.Stop();
        OutputPerfStats();
    }

    /// <summary>
    ///     Executes one scheduling slice of emulation work.
    /// </summary>
    /// <returns>True when the next slice should begin immediately, otherwise false.</returns>
    private bool RunSlice() {
        InitializeSliceTimer();
        long sliceStartTicks = _sliceStopwatch.ElapsedTicks;
        long sliceStartCycles = _cpuState.Cycles;
        int targetCycles = _cyclesBudgeter.GetNextSliceBudget();

        _executionStateSlice.CyclesAllocatedForSlice = targetCycles;
        _dualPic.AddTick();

        while (_cpuState.IsRunning) {
            if (!_dualPic.RunQueue()) {
                break;
            }

            while (_cpuState.IsRunning && _executionStateSlice.CyclesUntilReevaluation > 0) {
                if (_emulatorBreakpointsManager.HasActiveBreakpoints) {
                    _emulatorBreakpointsManager.TriggerBreakpoints();
                }
                _pauseHandler.WaitIfPaused();
                _cpu.ExecuteNext();
            }
        }

        _dualPic.RunQueue();
        _performanceMeasurer.UpdateValue(_cpuState.Cycles);
        UpdateAdaptiveCycleBudget(sliceStartTicks, sliceStartCycles);
        return HandleSliceTiming();
    }

    /// <summary>
    ///     Outputs accumulated performance statistics to the logger.
    /// </summary>
    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            long cyclesPerSeconds = _performanceMeasurer.AverageValuePerSecond;
            long elapsedTimeInMilliseconds = _performanceStopwatch.ElapsedMilliseconds;
            _loggerService.Warning(
                "Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.",
                _cpuState.Cycles, elapsedTimeInMilliseconds, cyclesPerSeconds);
        }
    }

    /// <summary>
    ///     Runs emulation from <paramref name="startAddress" /> until <paramref name="endAddress" /> is reached
    ///     or execution stops for another reason.
    /// </summary>
    /// <param name="startAddress">Address at which execution should begin.</param>
    /// <param name="endAddress">Address at which execution should stop.</param>
    internal void RunFromUntil(SegmentedAddress startAddress, SegmentedAddress endAddress) {
        _cpuState.IpSegmentedAddress = startAddress;
        ResetSliceTimer();
        while (_cpuState.IsRunning && _cpuState.IpSegmentedAddress != endAddress) {
            bool runImmediately;
            do {
                runImmediately = RunSlice();
            } while (runImmediately && _cpuState.IsRunning && _cpuState.IpSegmentedAddress != endAddress);
        }
    }

    /// <summary>
    ///     Increases the emulated CPU cycle budget.
    /// </summary>
    public void IncreaseCycles() {
        _cyclesLimiter.IncreaseCycles();
    }

    /// <summary>
    ///     Decreases the emulated CPU cycle budget.
    /// </summary>
    public void DecreaseCycles() {
        _cyclesLimiter.DecreaseCycles();
    }

    /// <summary>
    ///     Initializes the slice timer if it has not been started yet.
    /// </summary>
    private void InitializeSliceTimer() {
        if (_sliceInitialized) {
            return;
        }

        _nextSliceTick = _sliceStopwatch.ElapsedTicks;
        _sliceInitialized = true;
    }

    /// <summary>
    ///     Handles slice timing, waiting if necessary to respect the configured slice duration.
    /// </summary>
    /// <returns>True when another slice should start immediately; otherwise false.</returns>
    private bool HandleSliceTiming() {
        if (!_sliceInitialized || !_cpuState.IsRunning) {
            return false;
        }

        _nextSliceTick += _sliceDurationTicks;
        bool waited = HighResolutionWaiter.WaitUntil(_sliceStopwatch, _nextSliceTick);
        return _cpuState.IsRunning && !waited;
    }

    /// <summary>
    ///     Resets the slice timer so the next call will reinitialize timing.
    /// </summary>
    private void ResetSliceTimer() {
        _sliceInitialized = false;
        _nextSliceTick = _sliceStopwatch.ElapsedTicks;
    }

    /// <summary>
    ///     Updates the adaptive cycle budget based on the actual duration of the executed slice.
    /// </summary>
    private void UpdateAdaptiveCycleBudget(long sliceStartTicks, long sliceStartCycles) {
        if (!_cpuState.IsRunning) {
            return;
        }

        long elapsedTicks = _sliceStopwatch.ElapsedTicks - sliceStartTicks;
        if (elapsedTicks <= 0) {
            return;
        }

        double elapsedMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
        if (elapsedMilliseconds <= 0) {
            return;
        }

        long cyclesExecuted = _cpuState.Cycles - sliceStartCycles;
        if (cyclesExecuted <= 0) {
            return;
        }

        _cyclesBudgeter.UpdateBudget(elapsedMilliseconds, cyclesExecuted, _cpuState.IsRunning);
    }

    /// <summary>
    ///     Responds to pause state changes by resetting the slice timer.
    /// </summary>
    private void OnPauseStateChanged() {
        ResetSliceTimer();
    }
}
