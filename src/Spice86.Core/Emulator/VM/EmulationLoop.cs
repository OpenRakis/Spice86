namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Gdb;
using Spice86.Core.Emulator.VM.Pause;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// Runs the emulation loop in a dedicated thread. <br/>
/// Also, calls the DMA Controller once in order to start the DMA thread loop for DMA transfers. <br/>
/// On Pause, triggers a GDB breakpoint.
/// </summary>
public class EmulationLoop : IPauseable {
    private readonly ILoggerService _loggerService;
    private readonly Cpu _cpu;
    private readonly State _cpuState;
    private readonly Devices.Timer.Timer _timer;
    private readonly MachineBreakpoints _machineBreakpoints;
    private readonly DmaController _dmaController;
    private readonly GdbCommandHandler? _gdbCommandHandler;
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// Whether we check for breakpoints in the emulation loop.
    /// </summary>
    private readonly bool _listensToBreakpoints;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="cpuState">The emulated CPU State, so that we know when to stop.</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="listensToBreakpoints">Whether we react to breakpoints in the emulation loop.</param>
    /// <param name="machineBreakpoints">The class that stores emulation breakpoints.</param>
    /// <param name="dmaController">The DMA Controller, to start the DMA loop thread.</param>
    /// <param name="gdbCommandHandler">The GDB Command Handler, used to trigger a GDB breakpoint on pause.</param>
    public EmulationLoop(ILoggerService loggerService, Cpu cpu, State cpuState, Devices.Timer.Timer timer, bool listensToBreakpoints, MachineBreakpoints machineBreakpoints,
        DmaController dmaController, GdbCommandHandler? gdbCommandHandler) {
        _loggerService = loggerService;
        _cpu = cpu;
        _cpuState = cpuState;
        _timer = timer;
        _listensToBreakpoints = listensToBreakpoints;
        _machineBreakpoints = machineBreakpoints;
        _dmaController = dmaController;
        _gdbCommandHandler = gdbCommandHandler;
        _stopwatch = new();
    }

    /// <summary>
    /// Gets or sets whether the emulation loop thread is paused.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Starts and waits for the end of the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. This can occur if the target program is not supported (yet).</exception>
    public void Run() {
        FunctionHandler functionHandler = _cpu.FunctionHandler;
        try {
            StartRunLoop(functionHandler);
        } catch (HaltRequestedException) {
            // Actually a signal generated code requested Exit
            return;
        } catch (InvalidVMOperationException e) {
            e.Demystify();
            throw;
        } catch (Exception e) {
            e.Demystify();
            throw new InvalidVMOperationException(_cpuState, e);
        }
        _machineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
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
        _dmaController.StartDmaThread();
        RunLoop();
    }

    private void RunLoop() {
        _stopwatch.Start();
        while (_cpuState.IsRunning) {
            PauseIfAskedTo();
            _cpu.ExecuteNextInstruction();
            _timer.Tick();
        }
        _stopwatch.Stop();
        OutputPerfStats();
    }

    private void OutputPerfStats() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            long elapsedTimeMilliSeconds = _stopwatch.ElapsedMilliseconds;
            long cycles = _cpuState.Cycles;
            long cyclesPerSeconds = 0;
            if (elapsedTimeMilliSeconds > 0) {
                cyclesPerSeconds = (_cpuState.Cycles * 1000) / elapsedTimeMilliSeconds;
            }
            _loggerService.Warning("Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.", cycles, elapsedTimeMilliSeconds, cyclesPerSeconds);
        }
    }

    private bool GenerateUnconditionalGdbBreakpoint() {
        if (_gdbCommandHandler is null) {
            return false;
        }

        _gdbCommandHandler.Step();
        return true;
    }

    private void PauseIfAskedTo() {
        if (!IsPaused) {
            return;
        }

        if (!GenerateUnconditionalGdbBreakpoint()) {
            ThreadPause.SleepWhilePaused(this);
        }

        if (_listensToBreakpoints) {
            _machineBreakpoints.CheckBreakPoint();
        }
    }
}