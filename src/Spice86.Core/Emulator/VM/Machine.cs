namespace Spice86.Core.Emulator.VM;

using System.Diagnostics;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public sealed class Machine : IDisposable {
    private readonly ProgramExecutor _programExecutor;

    private bool _disposed;

    /// <summary>
    /// Gets or set if we record execution data, for reverse engineering purposes.
    /// </summary>
    public bool RecordData { get; set; }
    
    /// <summary>
    /// Handles all the callbacks, most notably interrupts.
    /// </summary>
    public CallbackHandler CallbackHandler { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public Cpu Cpu { get; }

    /// <summary>
    /// The emulated DOS kernel.
    /// </summary>
    public Dos Dos { get; }
    
    /// <summary>
    /// The GUI. Can be null in headless mode.
    /// </summary>
    public IMainWindowViewModel? Gui { get; }

    /// <summary>
    /// Gives the port read or write to the registered handler.
    /// </summary>
    public IOPortDispatcher IoPortDispatcher { get; }

    /// <summary>
    /// Contains all the breakpoints
    /// </summary>
    public MachineBreakpoints MachineBreakpoints { get; }
    
    /// <summary>
    /// Contains programmable chips, such as the PIC and the PIT
    /// </summary>
    public ProgrammableSubsystem ProgrammableSubsystem { get; } 

    /// <summary>
    /// The memory bus.
    /// </summary>
    public Memory Memory { get; }

    /// <summary>
    /// Contains the keyboard, mouse, and joystick.
    /// <remarks>BIOS keyboard/mouse interrupt handlers live in this subsystem.</remarks>
    /// </summary>
    public InputSubsystem InputSubsystem { get; }

    /// <summary>
    /// The basic input output system.
    /// </summary>
    public Bios Bios { get; }

    /// <summary>
    /// The DMA loop and DMA channels
    /// </summary>
    public DmaSubsystem DmaSubsystem { get; }
    
    /// <summary>
    /// Contains the VGA card, the VGA port handler, the VGA services, the VGA registers, the VGA renderer, the video interrupts, and VGA ROM.
    /// </summary>
    public VideoSubsystem VideoSubsystem { get; }
    
    /// <summary>
    /// Contains the PC Speaker, the external MIDI device (MT-32 or General MIDI), the FM Synth chips, and the sound cards
    /// </summary>
    public SoundSubsystem SoundSubsystem { get; }

    /// <summary>
    /// The emulator configuration.
    /// </summary>
    public Configuration Configuration { get; }
    
    /// <summary>
    /// Initializes a new instance
    /// <param name="machineCreationOptions">Describes how the machine will run, and what it will run.</param>
    /// </summary>
    public Machine(MachineCreationOptions machineCreationOptions) {
        _programExecutor = machineCreationOptions.ProgramExecutor;
        Configuration = machineCreationOptions.Configuration;
        Gui = machineCreationOptions.Gui;
        RecordData = machineCreationOptions.RecordData;

        IMemoryDevice ram = new Ram(MemoryConsts.EndOfHighMemoryArea);
        Memory = new Memory(ram, machineCreationOptions.Configuration);
        Cpu = new Cpu(this, machineCreationOptions.LoggerService, machineCreationOptions.ExecutionFlowRecorder, machineCreationOptions.RecordData);

        MachineBreakpoints = new MachineBreakpoints(this, machineCreationOptions.LoggerService);

        // IO devices
        IoPortDispatcher = new IOPortDispatcher(
            this,
            machineCreationOptions.LoggerService,
            machineCreationOptions.Configuration);
        Cpu.IoPortDispatcher = IoPortDispatcher;

        DmaSubsystem = new(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService, Gui);

        ProgrammableSubsystem = new(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService, machineCreationOptions.CounterConfigurator);

        MouseDevice = new Mouse(this, machineCreationOptions.Gui, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(MouseDevice);
        
        // Services
        CallbackHandler = new CallbackHandler(this, machineCreationOptions.LoggerService, MemoryMap.InterruptHandlersSegment);
        Cpu.CallbackHandler = CallbackHandler;
        
        InputSubsystem = new(this, machineCreationOptions.Gui, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);

        Bios = new(this, machineCreationOptions.LoggerService);

        VideoSubsystem = new(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService, Gui);

        SoundSubsystem = new(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);

        MouseDriver = new MouseDriver(Cpu, Memory, MouseDevice, machineCreationOptions.Gui, VideoSubsystem.VgaFunctions, machineCreationOptions.LoggerService);
        var mouseInt33Handler = new MouseInt33Handler(this, machineCreationOptions.LoggerService, MouseDriver);
        RegisterCallbackHandler(mouseInt33Handler);
        var mouseIrq12Handler = new BiosMouseInt74Handler(MouseDriver, ProgrammableSubsystem.DualPic, this, machineCreationOptions.LoggerService);
        RegisterCallbackHandler(mouseIrq12Handler);
        var mouseCleanupHandler = new CustomMouseInt90Handler(MouseDriver, this, machineCreationOptions.LoggerService);
        RegisterCallbackHandler(mouseCleanupHandler);

        // Initialize DOS.
        Dos = new Dos(this, machineCreationOptions.LoggerService);
        Dos.Initialize(SoundSubsystem.SoundBlaster, machineCreationOptions.Configuration);
    }

    /// <summary>
    /// The mouse device hardware abstraction.
    /// </summary>
    public IMouseDevice MouseDevice { get; }

    /// <summary>
    /// The mouse driver.
    /// </summary>
    public IMouseDriver MouseDriver { get; }
    
    /// <summary>
    /// Registers a callback, such as an interrupt handler.
    /// </summary>
    /// <param name="callback">The callback implementation.</param>
    public void RegisterCallbackHandler(ICallback callback) {
        CallbackHandler.AddCallback(callback);
    }

    /// <summary>
    /// Registers a I/O port handler, such as a sound card.
    /// </summary>
    /// <param name="ioPortHandler">The I/O port handler.</param>
    /// <exception cref="ArgumentException"></exception>
    public void RegisterIoPortHandler(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(IoPortDispatcher);
    }

    /// <summary>
    /// Installs all the callback in the dispatch table in emulated memory.
    /// </summary>
    public void InstallAllCallbacksInInterruptTable() {
        CallbackHandler.InstallAllCallbacksInInterruptTable();
    }

    /// <summary>
    /// Peeks at the return address.
    /// </summary>
    /// <returns>The return address string.</returns>
    public string PeekReturn() {
        return SegmentedAddress.ToString(Cpu.FunctionHandlerInUse.PeekReturnAddressOnMachineStackForCurrentFunction());
    }

    /// <summary>
    /// Implements the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. This can occur if the target program is not supported (yet).</exception>
    public void Run() {
        State state = Cpu.State;
        FunctionHandler functionHandler = Cpu.FunctionHandler;
        DmaSubsystem.Run();
        if (Debugger.IsAttached) {
            try {
                StartRunLoop(functionHandler, state);
            } catch (HaltRequestedException) {
                // Actually a signal generated code requested Exit
                Dispose(disposing: true);
            }
        } else {
            try {
                StartRunLoop(functionHandler, state);
            } catch (InvalidVMOperationException e) {
                e.Demystify();
                throw;
            } catch (HaltRequestedException) {
                // Actually a signal generated code requested Exit
                Dispose(disposing: true);
            } catch (Exception e) {
                e.Demystify();
                throw new InvalidVMOperationException(this, e);
            }
        }
        MachineBreakpoints.OnMachineStop();
        functionHandler.Ret(CallType.MACHINE);
    }

    private void StartRunLoop(FunctionHandler functionHandler, State state) {
        // Entry could be overridden and could throw exceptions
        functionHandler.Call(CallType.MACHINE, state.CS, state.IP, null, null, "entry", false);
        RunLoop();
    }

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    public bool IsPaused { get; private set; }

    private bool _exitEmulationLoop;

    /// <summary>
    /// Forces the emulation thread to exit.
    /// </summary>
    public void ExitEmulationLoop() => _exitEmulationLoop = true;

    private void RunLoop() {
        _exitEmulationLoop = false;
        while (Cpu.IsRunning && !_exitEmulationLoop && !_disposed) {
            PauseIfAskedTo();
            if (RecordData) {
                MachineBreakpoints.CheckBreakPoint();
            }
            Cpu.ExecuteNextInstruction();
            ProgrammableSubsystem.Timer.Tick();
        }
    }

    private void PauseIfAskedTo() {
        if(Gui?.PauseEmulatorOnStart == true) {
            Gui?.PauseEmulationOnStart();
            Gui?.WaitForContinue();
        }
        if (Gui?.IsPaused == true) {
            IsPaused = true;
            if (!_programExecutor.Step()) {
                Gui.IsPaused = true;
                Gui?.WaitForContinue();
            }
            IsPaused = false;
        }
    }

    /// <summary>
    /// Releases all resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                DmaSubsystem.Dispose();
                SoundSubsystem.Dispose();
                MachineBreakpoints.Dispose();
            }
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}