namespace Spice86.Core.Emulator.VM;

using Spice86.Core.CLI;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// Emulates an IBM PC
/// </summary>
public sealed class Machine : IDisposable {
    private readonly ProgramExecutor _programExecutor;
    private readonly List<DmaChannel> _dmaDeviceChannels = new();
    private readonly Thread _dmaThread;
    private bool _exitDmaLoop;
    private bool _dmaThreadStarted;
    private readonly ManualResetEvent _dmaResetEvent = new(true);

    private bool _disposed;

    /// <summary>
    /// Gets or set if we record execution data, for reverse engineering purposes.
    /// </summary>
    public bool RecordData { get; set; }
    
    /// <summary>
    /// Memory mapped BIOS values.
    /// </summary>
    public BiosDataArea BiosDataArea { get; set; }

    /// <summary>
    /// INT11H handler.
    /// </summary>
    public BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    /// <summary>
    /// INT9H handler.
    /// </summary>
    public BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    /// <summary>
    /// Handles all the callbacks, most notably interrupts.
    /// </summary>
    public CallbackHandler CallbackHandler { get; }
    
    private InterruptInstaller InterruptInstaller { get; }

    private AssemblyRoutineInstaller AssemblyRoutineInstaller { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public Cpu Cpu { get; }

    /// <summary>
    /// DOS Services.
    /// </summary>
    public Dos Dos { get; }

    /// <summary>
    /// The Gravis Ultrasound sound card.
    /// </summary>
    public GravisUltraSound GravisUltraSound { get; }

    /// <summary>
    /// The GUI. Can be null in headless mode.
    /// </summary>
    public IGui? Gui { get; }

    /// <summary>
    /// Gives the port read or write to the registered handler.
    /// </summary>
    public IOPortDispatcher IoPortDispatcher { get; }

    /// <summary>
    /// A gameport joystick
    /// </summary>
    public Joystick Joystick { get; }

    /// <summary>
    /// An IBM PC Keyboard
    /// </summary>
    public Keyboard Keyboard { get; }

    /// <summary>
    /// INT16H handler.
    /// </summary>
    public KeyboardInt16Handler KeyboardInt16Handler { get; }

    /// <summary>
    /// Contains all the breakpoints
    /// </summary>
    public MachineBreakpoints MachineBreakpoints { get; }

    /// <summary>
    /// The memory bus.
    /// </summary>
    public IMemory Memory { get; }

    /// <summary>
    /// The General MIDI (MPU-401) or MT-32 device.
    /// </summary>
    public Midi MidiDevice { get; }

    /// <summary>
    /// PC Speaker device.
    /// </summary>
    public PcSpeaker PcSpeaker { get; }

    /// <summary>
    /// The dual programmable interrupt controllers.
    /// </summary>
    public DualPic DualPic { get; }

    /// <summary>
    /// The Sound Blaster card.
    /// </summary>
    public SoundBlaster SoundBlaster { get; }

    /// <summary>
    /// INT15H handler.
    /// </summary>
    public SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    /// <summary>
    /// INT1A handler.
    /// </summary>
    public SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    /// <summary>
    /// The Programmable Interrupt Timer
    /// </summary>
    public Timer Timer { get; }

    /// <summary>
    /// INT8H handler.
    /// </summary>
    public TimerInt8Handler TimerInt8Handler { get; }

    /// <summary>
    /// The VGA Card.
    /// </summary>
    public IVideoCard VgaCard { get; }
    
    /// <summary>
    /// The Vga Registers
    /// </summary>
    public VideoState VgaRegisters { get; set; }
    
    /// <summary>
    /// The VGA port handler
    /// </summary>
    public IIOPortHandler VgaIoPortHandler { get; }

    /// <summary>
    /// The class that handles converting video memory to a bitmap 
    /// </summary>
    public readonly IVgaRenderer VgaRenderer;
    
    /// <summary>
    /// The Video BIOS interrupt handler.
    /// </summary>
    public IVideoInt10Handler VideoInt10Handler { get; }
    
    /// <summary>
    /// The Video Rom containing fonts and other data.
    /// </summary>
    public VgaRom VgaRom { get; }

    /// <summary>
    /// The DMA controller.
    /// </summary>
    public DmaController DmaController { get; }
    
    /// <summary>
    /// The OPL3 FM Synth chip.
    /// </summary>
    public OPL3FM OPL3FM { get; }

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

        IMemoryDevice ram = new Ram(A20Gate.EndOfHighMemoryArea);
        Memory = new Memory(ram, machineCreationOptions.Configuration);
        BiosDataArea = new BiosDataArea(Memory);
        Cpu = new Cpu(this, machineCreationOptions.LoggerService, machineCreationOptions.ExecutionFlowRecorder, machineCreationOptions.RecordData);

        // Breakpoints
        MachineBreakpoints = new MachineBreakpoints(this, machineCreationOptions.LoggerService);

        // IO devices
        IoPortDispatcher = new IOPortDispatcher(
            this,
            machineCreationOptions.LoggerService,
            machineCreationOptions.Configuration);
        Cpu.IoPortDispatcher = IoPortDispatcher;

        DmaController = new DmaController(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(DmaController);

        DualPic = new DualPic(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(DualPic);

        VgaRegisters = new VideoState();
        VgaIoPortHandler = new VgaIoPortHandler(this, machineCreationOptions.LoggerService, machineCreationOptions.Configuration, VgaRegisters);
        RegisterIoPortHandler(VgaIoPortHandler);

        const uint videoBaseAddress = MemoryMap.GraphicVideoMemorySegment << 4;
        IVideoMemory vgaMemory = new VideoMemory(VgaRegisters);
        Memory.RegisterMapping(videoBaseAddress, vgaMemory.Size, vgaMemory);
        VgaRenderer = new Renderer(VgaRegisters, vgaMemory);
        VgaCard = new VgaCard(machineCreationOptions.Gui, VgaRenderer, machineCreationOptions.LoggerService);

        Timer = new Timer(this, machineCreationOptions.LoggerService, DualPic, VgaCard, machineCreationOptions.CounterConfigurator, machineCreationOptions.Configuration);
        RegisterIoPortHandler(Timer);
        Keyboard = new Keyboard(this, machineCreationOptions.LoggerService, machineCreationOptions.Gui, machineCreationOptions.Configuration);
        RegisterIoPortHandler(Keyboard);
        MouseDevice = new Mouse(this, machineCreationOptions.Gui, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(MouseDevice);
        Joystick = new Joystick(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(Joystick);
        PcSpeaker = new PcSpeaker(this, machineCreationOptions.LoggerService, machineCreationOptions.Configuration);
        RegisterIoPortHandler(PcSpeaker);
        OPL3FM = new OPL3FM(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(OPL3FM);
        SoundBlaster = new SoundBlaster(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService, new SoundBlasterHardwareConfig(7, 1, 5));
        RegisterIoPortHandler(SoundBlaster);
        GravisUltraSound = new GravisUltraSound(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(GravisUltraSound);
        MidiDevice = new Midi(this, machineCreationOptions.Configuration, machineCreationOptions.LoggerService);
        RegisterIoPortHandler(MidiDevice);

        // Services
        CallbackHandler = new CallbackHandler(this, machineCreationOptions.LoggerService);
        Cpu.CallbackHandler = CallbackHandler;
        // memoryAsmWriter is common to InterruptInstaller and AssemblyRoutineInstaller so that they both write at the same address (Bios Segment F000)
        MemoryAsmWriter memoryAsmWriter = new MemoryAsmWriter(Memory, new SegmentedAddress(MemoryMap.InterruptHandlersSegment, 0), CallbackHandler);
        InterruptInstaller =
            new InterruptInstaller(new InterruptVectorTable(Memory), memoryAsmWriter, Cpu.FunctionHandler);
        AssemblyRoutineInstaller = new AssemblyRoutineInstaller(memoryAsmWriter, Cpu.FunctionHandler);
        
        VgaRom = new VgaRom();
        Memory.RegisterMapping(MemoryMap.VideoBiosSegment << 4, VgaRom.Size, VgaRom);
        VgaFunctions = new VgaFunctionality(Memory, IoPortDispatcher, BiosDataArea, VgaRom);
        VideoInt10Handler = new VgaBios(this, VgaFunctions, BiosDataArea, machineCreationOptions.LoggerService);
        
        TimerInt8Handler = new TimerInt8Handler(this, machineCreationOptions.LoggerService);
        BiosKeyboardInt9Handler = new BiosKeyboardInt9Handler(this, this.Memory, BiosDataArea, machineCreationOptions.LoggerService);
        
        BiosEquipmentDeterminationInt11Handler = new BiosEquipmentDeterminationInt11Handler(this, machineCreationOptions.LoggerService);
        SystemBiosInt15Handler = new SystemBiosInt15Handler(this, machineCreationOptions.LoggerService);
        KeyboardInt16Handler = new KeyboardInt16Handler(
            this,
            machineCreationOptions.LoggerService,
            BiosKeyboardInt9Handler.BiosKeyboardBuffer);

        SystemClockInt1AHandler = new SystemClockInt1AHandler(
            this,
            machineCreationOptions.LoggerService,
            TimerInt8Handler);

        MouseDriver = new MouseDriver(Cpu, Memory, MouseDevice, machineCreationOptions.Gui, VgaFunctions, machineCreationOptions.LoggerService);
        Dos = new Dos(this, Configuration, Memory, machineCreationOptions.LoggerService);

        if (Configuration.InitializeDOS is not false) {
            // Register the interrupt handlers
            RegisterInterruptHandler(VideoInt10Handler);
            RegisterInterruptHandler(TimerInt8Handler);
            RegisterInterruptHandler(BiosKeyboardInt9Handler);
            RegisterInterruptHandler(BiosEquipmentDeterminationInt11Handler);
            RegisterInterruptHandler(SystemBiosInt15Handler);
            RegisterInterruptHandler(KeyboardInt16Handler);
            RegisterInterruptHandler(SystemClockInt1AHandler);
            // Initialize DOS.
            Dos.Initialize(SoundBlaster, machineCreationOptions.Configuration);

            var mouseInt33Handler = new MouseInt33Handler(this, machineCreationOptions.LoggerService, MouseDriver);
            RegisterInterruptHandler(mouseInt33Handler);

            var mouseIrq12Handler = new BiosMouseInt74Handler(DualPic, Memory);
            RegisterInterruptHandler(mouseIrq12Handler);

            SegmentedAddress mouseDriverAddress = AssemblyRoutineInstaller.InstallAssemblyRoutine(MouseDriver);
            mouseIrq12Handler.SetMouseDriverAddress(mouseDriverAddress);
        }
        _dmaThread = new Thread(DmaLoop) {
            Name = "DMAThread"
        };
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
    /// Defines all VGA high level functions, such as writing text to the screen.
    /// </summary>
    public IVgaFunctionality VgaFunctions { get; set; }

    /// <summary>
    /// Registers an interrupt handler
    /// </summary>
    /// <param name="interruptHandler">The interrupt handler to install.</param>
    public void RegisterInterruptHandler(IInterruptHandler interruptHandler) {
        InterruptInstaller.InstallInterruptHandler(interruptHandler);
    }

    /// <summary>
    /// Registers a I/O port handler, such as a sound card.
    /// </summary>
    /// <param name="ioPortHandler">The I/O port handler.</param>
    /// <exception cref="ArgumentException"></exception>
    public void RegisterIoPortHandler(IIOPortHandler ioPortHandler) {
        ioPortHandler.InitPortHandlers(IoPortDispatcher);

        if (ioPortHandler is not IDmaDevice8 dmaDevice) {
            return;
        }

        if (dmaDevice.Channel < 0 || dmaDevice.Channel >= DmaController.Channels.Count) {
            throw new ArgumentException("Invalid DMA channel on DMA device.");
        }

        DmaController.Channels[dmaDevice.Channel].Device = dmaDevice;
        _dmaDeviceChannels.Add(DmaController.Channels[dmaDevice.Channel]);
    }

    /// <summary>
    /// https://techgenix.com/direct-memory-access/
    /// </summary>
    private void DmaLoop() {
        while (Cpu.IsRunning && !_exitDmaLoop && !_exitEmulationLoop && !_disposed) {
            for (int i = 0; i < _dmaDeviceChannels.Count; i++) {
                DmaChannel dmaChannel = _dmaDeviceChannels[i];
                bool transfered = dmaChannel.Transfer(Memory);
                if (!_exitDmaLoop && !transfered) {
                    _dmaResetEvent.WaitOne(Timeout.Infinite);
                }
            }
        }
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
        if (!_dmaThreadStarted) {
            _dmaThread.Start();
            _dmaThreadStarted = true;
        }
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
    public bool IsPaused { get; set; }

    private bool _exitEmulationLoop;

    /// <summary>
    /// Forces the emulation loop to exit.
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
            Timer.Tick();
        }
    }

    /// <summary>
    /// Performs DMA transfers when invoked.
    /// </summary>
    public void PerformDmaTransfers() {
        if (!_disposed && !_exitDmaLoop) {
            _dmaResetEvent.Set();
        }
    }

    private void PauseIfAskedTo() {
        if (IsPaused) {
            if (!_programExecutor.Step()) {
                while (IsPaused) {
                    Thread.Sleep(1);
                }
            }
        }
    }

    /// <summary>
    /// Releases all resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                _dmaResetEvent.Set();
                _exitDmaLoop = true;
                if (_dmaThread.IsAlive && _dmaThreadStarted) {
                    _dmaThread.Join();
                }
                _dmaResetEvent.Dispose();
                MidiDevice.Dispose();
                SoundBlaster.Dispose();
                OPL3FM.Dispose();
                PcSpeaker.Dispose();
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