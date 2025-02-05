namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Interfaces;

using System.Diagnostics;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.Timer;


/// <summary>
/// Runs the emulation loop in a dedicated thread. <br/>
/// On Pause, triggers a GDB breakpoint.
/// </summary>
public class EmulationLoop : ICyclesLimiter {
    private readonly ILoggerService _loggerService;
    private readonly IInstructionExecutor _cpu;
    private readonly FunctionHandler _functionHandler;
    private readonly State _cpuState;
    private readonly Timer _timer;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly PerformanceMeasurer _performanceMeasurer = new();
    private readonly Stopwatch _stopwatch;
    private readonly DmaController _dmaController;
    private const int CyclesUp = 1000;
    private const int CyclesDown = 1000;
    private const int MaxCyclesPerMs = 60000;
    private const int MinCyclesPerMs = 100;

    private long _lastTime;

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }
    public int TargetCpuCylesPerMs { get; set; } = ICyclesLimiter.RealModeCpuCylesPerMs;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="dmaController">The Direct Memory Access controller chip.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    public EmulationLoop(ILoggerService loggerService,
        FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState,
        Timer timer, EmulatorBreakpointsManager emulatorBreakpointsManager,
        DmaController dmaController,
        IPauseHandler pauseHandler) {
        _loggerService = loggerService;
        _dmaController = dmaController;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _timer = timer;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _stopwatch = new();
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
        _stopwatch.Start();
        _lastTime = _stopwatch.ElapsedMilliseconds;
        _cpu.SignalEntry();
        while (_cpuState.IsRunning) {
            RunEmulatorComponentsInTurn();
            AdjustCycles();
        }
        _stopwatch.Stop();
        OutputPerfStats();
    }

    private void RunEmulatorComponentsInTurn() {
        _emulatorBreakpointsManager.CheckExecutionBreakPoints();
        _pauseHandler.WaitIfPaused();
        _cpu.ExecuteNext();
        _performanceMeasurer.UpdateValue(_cpuState.Cycles);
        _timer.Tick();
        _dmaController.PerformDmaTransfers();
    }

    private void AdjustCycles() {
        long currentTime = _stopwatch.ElapsedMilliseconds;
        long elapsedTime = currentTime - _lastTime;

        long targetCycles = Math.Max(TargetCpuCylesPerMs, 1) * elapsedTime;
        long executedCycles = Math.Max(_performanceMeasurer.ValuePerMillisecond, 1) * elapsedTime;
        if (executedCycles > targetCycles) {
            if(_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
                _loggerService.Debug("Executed {ExecutedCycles} instructions in {ElapsedTime}ms. Target was {TargetCycles} instructions.",
                    executedCycles, elapsedTime, targetCycles);
            }
            Thread.Sleep(1);
        }
        _lastTime = currentTime;
    }

    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            long cyclesPerSeconds = _performanceMeasurer.AverageValuePerSecond;
            long elapsedTimeInMilliseconds = _stopwatch.ElapsedMilliseconds;
            _loggerService.Warning(
                "Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.",
                _cpuState.Cycles, elapsedTimeInMilliseconds, cyclesPerSeconds);
        }
    }

    public void IncreaseCycles() {
        TargetCpuCylesPerMs = Math.Min(TargetCpuCylesPerMs + CyclesUp, MaxCyclesPerMs);
    }

    public void DecreaseCycles() {
        TargetCpuCylesPerMs = Math.Max(TargetCpuCylesPerMs - CyclesDown, MinCyclesPerMs);
    }
}
