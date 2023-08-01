using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;

namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Gdb;

using System.Diagnostics;

/// <summary>
/// Runs the emulation loop in a dedicated thread. <br/>
/// Also, calls the DMA Controller once in order to start the DMA thread loop for DMA transfers. <br/>
/// On Pause, triggers a GDB breakpoint.
/// </summary>
public class EmulationLoop {
    private readonly Cpu _cpu;
    private readonly Devices.Timer.Timer _timer;
    private readonly MachineBreakpoints _machineBreakpoints;
    private readonly DmaController _dmaController;
    private readonly GdbCommandHandler? _gdbCommandHandler;
    
    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; set; }
    
    /// <summary>
    /// Gets if we record execution data, for reverse engineering purposes.
    /// </summary>
    public bool RecordData { get; }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="cpu">The emulated CPU, so the emulation loop can call ExecuteNextInstruction().</param>
    /// <param name="timer">The timer device, so the emulation loop can call Tick()</param>
    /// <param name="recordData">Whether we record machine code execution data.</param>
    /// <param name="machineBreakpoints">The class that stores emulation breakpoints.</param>
    /// <param name="dmaController">The DMA Controller, to start the DMA loop thread.</param>
    /// <param name="gdbCommandHandler">The GDB Command Handler, used to trigger a GDB breakpoint on pause.</param>
    public EmulationLoop(Cpu cpu, Devices.Timer.Timer timer, bool recordData, MachineBreakpoints machineBreakpoints,
        DmaController dmaController, GdbCommandHandler? gdbCommandHandler) {
        _cpu = cpu;
        _timer = timer;
        RecordData = recordData;
        _machineBreakpoints = machineBreakpoints;
        _dmaController = dmaController;
        _gdbCommandHandler = gdbCommandHandler;
    }
    
    /// <summary>
    /// Starts and waits for the end of the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. This can occur if the target program is not supported (yet).</exception>
    public void Run() {
        State state = _cpu.State;
        FunctionHandler functionHandler = _cpu.FunctionHandler;
        if (Debugger.IsAttached) {
            try {
                StartRunLoop(functionHandler, state);
            } catch (HaltRequestedException) {
                // Actually a signal generated code requested Exit
                return;
            }
        } else {
            try {
                StartRunLoop(functionHandler, state);
            } catch (InvalidVMOperationException e) {
                e.Demystify();
                throw;
            } catch (HaltRequestedException) {
                // Actually a signal generated code requested Exit
                return;
            } catch (Exception e) {
                e.Demystify();
                throw new InvalidVMOperationException(_cpu.State, e);
            }
        }
        _machineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    /// <summary>
    /// Forces the emulation loop to exit.
    /// </summary>
    internal void Exit() => _cpu.IsRunning = false;

    private void StartRunLoop(FunctionHandler functionHandler, State state) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, state.CS, state.IP, null, null, "entry", false);
        _dmaController.StartDmaThread();
        if (RecordData) {
            RunLoopWhileRecordingExecutionData();
        } else {
            RunLoop();
        }
    }
    
    private void RunLoopWhileRecordingExecutionData() {
        while (_cpu.IsRunning) {
            PauseIfAskedTo();
            _machineBreakpoints.CheckBreakPoint();
            _cpu.ExecuteNextInstruction();
            _timer.Tick();
        }
    }
    
    private bool GdbStep() {
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

        if (!GdbStep()) {
            return;
        }

        while (IsPaused) {
            Thread.Sleep(1);
        }
    }
    
    private void RunLoop() {
        while (_cpu.IsRunning) {
            PauseIfAskedTo();
            _cpu.ExecuteNextInstruction();
            _timer.Tick();
        }
    }
}