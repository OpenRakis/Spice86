namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Timer = Spice86.Core.Emulator.Devices.Timer.Timer;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Shared.Interfaces;
using Spice86.Shared.Diagnostics;
using System.Diagnostics;


/// <summary>
/// Runs the emulation loop in a dedicated thread. <br/>
/// On Pause, triggers a GDB breakpoint.
/// </summary>
public class EmulationLoop {
    private readonly ILoggerService _loggerService;
    private readonly IInstructionExecutor _cpu;
    private readonly FunctionHandler _functionHandler;
    private readonly State _cpuState;
    private readonly Timer _timer;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IPauseHandler _pauseHandler;
    private readonly PerformanceMeasurer _performanceMeasurer;
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="functionHandler">The class that handles function calls in the machine code.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="emulatorBreakpointsManager">The class that stores emulation breakpoints.</param>
    /// <param name="pauseHandler">The emulation pause handler.</param>
    public EmulationLoop(ILoggerService loggerService, FunctionHandler functionHandler, IInstructionExecutor cpu, State cpuState, Timer timer, EmulatorBreakpointsManager emulatorBreakpointsManager, IPauseHandler pauseHandler) {
        _loggerService = loggerService;
        _cpu = cpu;
        _functionHandler = functionHandler;
        _cpuState = cpuState;
        _timer = timer;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _pauseHandler = pauseHandler;
        _performanceMeasurer = new PerformanceMeasurer();
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
        _functionHandler.Ret(CallType.MACHINE);
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
        functionHandler.Call(CallType.MACHINE, _cpuState.CS, _cpuState.IP, null, null, "entry", false);
        RunLoop();
    }

    private void RunLoop() {
        if (_cpu is CfgCpu cfgCpu) {
            cfgCpu.SignalEntry();
        }
        _stopwatch.Start();
        while (_cpuState.IsRunning) {
            _emulatorBreakpointsManager.CheckBreakPoint();
            _pauseHandler.WaitIfPaused();
            _cpu.ExecuteNext();
            _performanceMeasurer.UpdateValue(_cpuState.Cycles);
            _timer.Tick();
        }
        _stopwatch.Stop();
        OutputPerfStats();
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
}