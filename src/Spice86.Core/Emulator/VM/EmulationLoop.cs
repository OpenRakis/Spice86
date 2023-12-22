using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;

namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Gdb;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// Runs the emulation loop in a dedicated thread. <br/>
/// Also, calls the DMA Controller once in order to start the DMA thread loop for DMA transfers. <br/>
/// On Pause, triggers a GDB breakpoint.
/// </summary>
public sealed class EmulationLoop : IDisposable {
    private readonly ILoggerService _loggerService;
    private readonly Cpu _cpu;
    private readonly State _cpuState;
    private readonly Devices.Timer.Timer _timer;
    private readonly MachineBreakpoints _machineBreakpoints;
    private readonly DmaController _dmaController;
    private readonly GdbCommandHandler? _gdbCommandHandler;
    private readonly Stopwatch _stopwatch;
    private readonly ManualResetEvent _manualResetEvent = new(true);

    /// <summary>
    /// Gets if we check for breakpoints in the emulation loop.
    /// </summary>
    private readonly bool _listensToBreakpoints;
    private bool _disposed;
    private bool _isPaused;

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
        _pauseProcedure = _gdbCommandHandler is not null ? _gdbCommandHandler.Step : (() => _manualResetEvent.WaitOne());
    }
    
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
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused {
        get => _isPaused;
        set {
            _isPaused = value;
            if (_isPaused) {
                Pause();
            } else {
                Resume();
            }
        }
    }

    private void Pause() {
        _manualResetEvent.Reset();
        _pauseProcedure ??= () => _manualResetEvent.WaitOne();
    }

    /// <summary>
    /// Forces the emulation loop to exit.
    /// </summary>
    internal void Exit() {
        _threadMustExit = true;
        _cpuState.IsRunning = false;
        IsPaused = false;
    }

    private void StartRunLoop(FunctionHandler functionHandler) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, _cpuState.CS, _cpuState.IP, null, null, "entry", false);
        _dmaController.StartDmaThread();
        RunLoop();
    }

    private Action _pauseProcedure;

    private bool _threadMustExit;
    
    private void RunLoop() {
        _stopwatch.Start();
        while (!_threadMustExit) {
            if(IsPaused) {
                _pauseProcedure.Invoke();
            }
            if(_listensToBreakpoints) {
                _machineBreakpoints.CheckBreakPoint();
            }
            _cpu.ExecuteNextInstruction();
            _timer.Tick();
        }
        OutputPerfStats();
    }

    private void OutputPerfStats() {
        _stopwatch.Stop();
        if (_loggerService.IsEnabled(Serilog.Events.LogEventLevel.Warning)) {
            long elapsedTimeMilliSeconds = _stopwatch.ElapsedMilliseconds;
            long cycles = _cpuState.Cycles;
            long cyclesPerSeconds = 0;
            if (elapsedTimeMilliSeconds > 0) {
                cyclesPerSeconds = (_cpuState.Cycles * 1000) / elapsedTimeMilliSeconds;
            }
            _loggerService.Warning("Executed {Cycles} instructions in {ElapsedTimeMilliSeconds}ms. {CyclesPerSeconds} Instructions per seconds on average over run.", cycles, elapsedTimeMilliSeconds, cyclesPerSeconds);
        }
    }

    private void Resume() {
        _manualResetEvent.Set();
    }

    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _manualResetEvent.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}