namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Diagnostics;
using Spice86.Shared.Interfaces;

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
    private readonly Stopwatch _performanceStopwatch = new();
    private readonly ICyclesLimiter _cyclesLimiter;
    private readonly InputEventHub _inputEventQueue;
    private readonly EmulationLoopScheduler.EmulationLoopScheduler _emulationLoopScheduler;

    /// <summary>
    /// Gets or sets whether the emulation is paused.
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
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    /// <param name="emulationLoopScheduler">The event scheduler.</param>
    /// <param name="inputEventQueue">Used to ensure that Mouse/Keyboard events are processed in the emulation thread.</param>
    /// <param name="cyclesLimiter">Limits the number of executed instructions per slice</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public EmulationLoop(FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState,
        EmulationLoopScheduler.EmulationLoopScheduler emulationLoopScheduler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, InputEventHub inputEventQueue,
        ICyclesLimiter cyclesLimiter, ILoggerService loggerService) {
        _loggerService = loggerService;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _emulationLoopScheduler = emulationLoopScheduler;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _inputEventQueue = inputEventQueue;
        _cyclesLimiter = cyclesLimiter;
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
        while (_cpuState.IsRunning) {
            do {
                if (_emulatorBreakpointsManager.HasActiveBreakpoints) {
                    _emulatorBreakpointsManager.CheckExecutionBreakPoints();
                }

                _pauseHandler.WaitIfPaused();
                _emulationLoopScheduler.ProcessEvents();
                _cpu.ExecuteNext();
                //_performanceMeasurer.UpdateValue(_cpuState.Cycles);
                _inputEventQueue.ProcessAllPendingInputEvents();
            } while (_cpuState.IsRunning);
        }

        _performanceStopwatch.Stop();
        OutputPerfStats();
    }

    /// <summary>
    ///     Outputs accumulated performance statistics to the logger.
    /// </summary>
    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            long elapsedTimeInMilliseconds = _performanceStopwatch.ElapsedMilliseconds;
            if (elapsedTimeInMilliseconds == 0) {
                elapsedTimeInMilliseconds = 1;
            }
            long cyclesPerSeconds = _cpuState.Cycles * 1000 / elapsedTimeInMilliseconds;
            _loggerService.Warning(
                "Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.",
                _cpuState.Cycles, elapsedTimeInMilliseconds, cyclesPerSeconds);
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
}
