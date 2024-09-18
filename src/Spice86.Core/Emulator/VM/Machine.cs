namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Sound.PCSpeaker;
using Spice86.Core.Emulator.Devices.Sound.Ymf262Emu;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;

/// <summary>
/// Centralizes many useful emulator devices and components for machine code overrides.
/// </summary>
public sealed class Machine : IDisposable {
    private bool _disposed;

    /// <summary>
    /// Memory mapped BIOS values.
    /// </summary>
    public BiosDataArea BiosDataArea { get; }

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

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public Cpu Cpu { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    public CfgCpu CfgCpu { get; }

    /// <summary>
    /// The emulated CPU state.
    /// </summary>
    public State CpuState { get; }

    /// <summary>
    /// DOS Services.
    /// </summary>
    public Dos Dos { get; }

    /// <summary>
    /// The Gravis Ultrasound sound card.
    /// </summary>
    public GravisUltraSound GravisUltraSound { get; }

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
    public EmulatorBreakpointsManager EmulatorBreakpointsManager { get; }

    /// <summary>
    /// The memory bus.
    /// </summary>
    public IMemory Memory { get; }

    /// <summary>
    /// The General MIDI or MT-32 device.
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
    /// INT12H handler.
    /// </summary>
    public SystemBiosInt12Handler SystemBiosInt12Handler { get; }

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
    public VgaCard VgaCard { get; }

    /// <summary>
    /// The VGA Registers
    /// </summary>
    public IVideoState VgaRegisters { get; set; }

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
    /// The internal software mixer for all sound channels.
    /// </summary>
    public SoftwareMixer SoftwareMixer { get; }

    /// <summary>
    /// The size of the conventional memory in kilobytes.
    /// </summary>
    public const uint ConventionalMemorySizeKb = 640;

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
    /// The pause handler for the emulation thread
    /// </summary>
    public IPauseHandler PauseHandler { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Machine"/> class.
    /// </summary>
    public Machine(BiosDataArea biosDataArea,
        BiosEquipmentDeterminationInt11Handler biosEquipmentDeterminationInt11Handler,
        BiosKeyboardInt9Handler biosKeyboardInt9Handler,
        CallbackHandler callbackHandler,
        Cpu cpu,
        CfgCpu cfgCpu,
        State cpuState,
        Dos dos,
        GravisUltraSound gravisUltraSound,
        IOPortDispatcher ioPortDispatcher,
        Joystick joystick,
        Keyboard keyboard,
        KeyboardInt16Handler keyboardInt16Handler,
        EmulatorBreakpointsManager emulatorBreakpointsManager,
        IMemory memory,
        Midi midiDevice,
        PcSpeaker pcSpeaker,
        DualPic dualPic,
        SoundBlaster soundBlaster,
        SystemBiosInt12Handler systemBiosInt12Handler,
        SystemBiosInt15Handler systemBiosInt15Handler,
        SystemClockInt1AHandler systemClockInt1AHandler,
        Timer timer,
        TimerInt8Handler timerInt8Handler,
        VgaCard vgaCard,
        IVideoState vgaRegisters,
        IIOPortHandler vgaIoPortHandler,
        IVgaRenderer vgaRenderer,
        IVideoInt10Handler videoInt10Handler,
        VgaRom vgaRom,
        DmaController dmaController,
        OPL3FM opl3FM,
        SoftwareMixer softwareMixer,
        IMouseDevice mouseDevice,
        IMouseDriver mouseDriver,
        IVgaFunctionality vgaFunctions,
        IPauseHandler pauseHandler) {
        BiosDataArea = biosDataArea;
        BiosEquipmentDeterminationInt11Handler = biosEquipmentDeterminationInt11Handler;
        BiosKeyboardInt9Handler = biosKeyboardInt9Handler;
        CallbackHandler = callbackHandler;
        Cpu = cpu;
        CfgCpu = cfgCpu;
        CpuState = cpuState;
        Dos = dos;
        GravisUltraSound = gravisUltraSound;
        IoPortDispatcher = ioPortDispatcher;
        Joystick = joystick;
        Keyboard = keyboard;
        KeyboardInt16Handler = keyboardInt16Handler;
        EmulatorBreakpointsManager = emulatorBreakpointsManager;
        Memory = memory;
        MidiDevice = midiDevice;
        PcSpeaker = pcSpeaker;
        DualPic = dualPic;
        SoundBlaster = soundBlaster;
        SystemBiosInt12Handler = systemBiosInt12Handler;
        SystemBiosInt15Handler = systemBiosInt15Handler;
        SystemClockInt1AHandler = systemClockInt1AHandler;
        Timer = timer;
        TimerInt8Handler = timerInt8Handler;
        VgaCard = vgaCard;
        VgaRegisters = vgaRegisters;
        VgaIoPortHandler = vgaIoPortHandler;
        VgaRenderer = vgaRenderer;
        VideoInt10Handler = videoInt10Handler;
        VgaRom = vgaRom;
        DmaController = dmaController;
        OPL3FM = opl3FM;
        SoftwareMixer = softwareMixer;
        MouseDevice = mouseDevice;
        MouseDriver = mouseDriver;
        VgaFunctions = vgaFunctions;
        PauseHandler = pauseHandler;
    }

    /// <summary>
    /// Releases all resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                MidiDevice.Dispose();
                SoundBlaster.Dispose();
                DmaController.Dispose();
                OPL3FM.Dispose();
                PcSpeaker.Dispose();
                SoftwareMixer.Dispose();
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