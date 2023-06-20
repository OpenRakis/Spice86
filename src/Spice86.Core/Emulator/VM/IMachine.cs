namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Joystick;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.SystemClock;
using Spice86.Core.Emulator.InterruptHandlers.Timer;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.OperatingSystem;
using Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Shared.Interfaces;

public interface IMachine {
    /// <summary>
    /// Whether we record execution data or not, for reverse engineering purposes.
    /// </summary>
    bool RecordData { get; set; }

    /// <summary>
    /// Memory mapped BIOS values.
    /// </summary>
    BiosDataArea BiosDataArea { get; set; }

    /// <summary>
    /// INT11H handler.
    /// </summary>
    BiosEquipmentDeterminationInt11Handler BiosEquipmentDeterminationInt11Handler { get; }

    /// <summary>
    /// INT9H handler.
    /// </summary>
    BiosKeyboardInt9Handler BiosKeyboardInt9Handler { get; }

    /// <summary>
    /// Handles all the callbacks, most notably interrupts.
    /// </summary>
    CallbackHandler CallbackHandler { get; }

    /// <summary>
    /// The emulated CPU.
    /// </summary>
    Cpu Cpu { get; }

    /// <summary>
    /// DOS Services.
    /// </summary>
    Dos Dos { get; }

    /// <summary>
    /// The Gravis Ultrasound sound card.
    /// </summary>
    GravisUltraSound GravisUltraSound { get; }

    /// <summary>
    /// The GUI. Can be null in headless mode.
    /// </summary>
    IGui? Gui { get; }

    /// <summary>
    /// Gives the port read or write to the registered handler.
    /// </summary>
    IOPortDispatcher IoPortDispatcher { get; }

    /// <summary>
    /// A gameport joystick
    /// </summary>
    Joystick Joystick { get; }

    /// <summary>
    /// An IBM PC Keyboard
    /// </summary>
    Keyboard Keyboard { get; }

    /// <summary>
    /// INT16H handler.
    /// </summary>
    KeyboardInt16Handler KeyboardInt16Handler { get; }

    /// <summary>
    /// Contains all the breakpoints
    /// </summary>
    MachineBreakpoints MachineBreakpoints { get; }

    /// <summary>
    /// The memory bus.
    /// </summary>
    Memory Memory { get; }

    /// <summary>
    /// The General MIDI (MPU-401) or MT-32 device.
    /// </summary>
    Midi MidiDevice { get; }

    /// <summary>
    /// PC Speaker device.
    /// </summary>
    PcSpeaker PcSpeaker { get; }

    /// <summary>
    /// The dual programmable interrupt controllers.
    /// </summary>
    DualPic DualPic { get; }

    /// <summary>
    /// The Sound Blaster card.
    /// </summary>
    SoundBlaster SoundBlaster { get; }

    /// <summary>
    /// INT15H handler.
    /// </summary>
    SystemBiosInt15Handler SystemBiosInt15Handler { get; }

    /// <summary>
    /// INT1A handler.
    /// </summary>
    SystemClockInt1AHandler SystemClockInt1AHandler { get; }

    /// <summary>
    /// The Programmable Interrupt Timer
    /// </summary>
    Timer Timer { get; }

    /// <summary>
    /// INT8H handler.
    /// </summary>
    TimerInt8Handler TimerInt8Handler { get; }

    /// <summary>
    /// The VGA Card.
    /// </summary>
    IVideoCard VgaCard { get; }

    /// <summary>
    /// The Vga Registers
    /// </summary>
    VideoState VgaRegisters { get; set; }

    /// <summary>
    /// The VGA port handler
    /// </summary>
    IIOPortHandler VgaIoPortHandler { get; }

    /// <summary>
    /// The Video BIOS interrupt handler.
    /// </summary>
    IVideoInt10Handler VideoInt10Handler { get; }

    /// <summary>
    /// The Video Rom containing fonts and other data.
    /// </summary>
    VgaRom VgaRom { get; }

    /// <summary>
    /// The EMS device driver.
    /// </summary>
    ExpandedMemoryManager? Ems { get; set; }

    /// <summary>
    /// The DMA controller.
    /// </summary>
    DmaController DmaController { get; }

    /// <summary>
    /// Gets the current DOS environment variables.
    /// </summary>
    EnvironmentVariables EnvironmentVariables { get; }

    /// <summary>
    /// The OPL3 FM Synth chip.
    /// </summary>
    OPL3FM OPL3FM { get; }

    /// <summary>
    /// The emulator configuration.
    /// </summary>
    Configuration Configuration { get; }

    ExtendedBiosDataArea? ExtendedBiosDataArea { get; set; }
    IMouseDevice MouseDevice { get; set; }
    IMouseInt33Handler MouseDriver { get; set; }
    IVgaFunctionality VgaFunctions { get; set; }

    /// <summary>
    /// Whether the emulation is paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// The code invoked when emulation pauses.
    /// </summary>
    event Action? Paused;

    /// <summary>
    /// The code invoked when emulation resumes.
    /// </summary>
    event Action? Resumed;

    /// <summary>
    /// Registers a callback, such as an interrupt handler.
    /// </summary>
    /// <param name="callback">The callback implementation.</param>
    void Register(ICallback callback);

    /// <summary>
    /// Registers a I/O port handler, such as a sound card.
    /// </summary>
    /// <param name="ioPortHandler">The I/O port handler.</param>
    /// <exception cref="ArgumentException"></exception>
    void Register(IIOPortHandler ioPortHandler);

    /// <summary>
    /// Returns a string that dumps the call stack.
    /// </summary>
    /// <returns>A string laying out the call stack.</returns>
    string DumpCallStack();

    /// <summary>
    /// Installs all the callback in the dispatch table in emulated memory.
    /// </summary>
    void InstallAllCallbacksInInterruptTable();

    /// <summary>
    /// Peeks at the return address.
    /// </summary>
    /// <returns>The return address string.</returns>
    string PeekReturn();

    /// <summary>
    /// Peeks at the return address.
    /// </summary>
    /// <param name="returnCallType">The expected call type.</param>
    /// <returns>The return address string.</returns>
    string PeekReturn(CallType returnCallType);

    /// <summary>
    /// Implements the emulation loop.
    /// </summary>
    /// <exception cref="InvalidVMOperationException">When an unhandled exception occurs. This can occur if the target program is not supported (yet).</exception>
    void Run();

    /// <summary>
    /// Forces the emulation loop to exit.
    /// </summary>
    void ExitEmulationLoop();

    /// <summary>
    /// Performs DMA transfers when invoked.
    /// </summary>
    void PerformDmaTransfers();

    /// <inheritdoc />
    void Dispose();
}