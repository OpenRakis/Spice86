namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Common.Callback;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of BIOS keyboard buffer handler (hardware interrupt 0x9, IRQ1)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly AvaloniaKeyConverter _scanCodeConverter = new();
    private readonly PS2Keyboard _keyboard;
    private static readonly SegmentedAddress CallbackLocation = new(0xf000, 0xe987);
    private readonly DualPic _dualPic;
    private readonly EmulationLoopRecall _emulationLoopRecall;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="dualPic">The interrupt controller, used to eventually acknowledge IRQ1 hardware interrupt.</param>
    /// <param name="keyboard">The keyboard device for direct port access.</param>
    /// <param name="biosKeyboardBuffer">The structure in emulated memory this interrupt handler writes to.</param>
    /// <param name="emulationLoopRecall">The class used to call the INT15H AH=4F interrupt for keyboard intercept.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory, Stack stack, State state,
        IFunctionHandlerProvider functionHandlerProvider, DualPic dualPic,
        PS2Keyboard keyboard, BiosKeyboardBuffer biosKeyboardBuffer,
        EmulationLoopRecall emulationLoopRecall, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        BiosKeyboardBuffer = biosKeyboardBuffer;
        _keyboard = keyboard;
        _dualPic = dualPic;
        _emulationLoopRecall = emulationLoopRecall;
    }

    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress savedAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = CallbackLocation;
        base.WriteAssemblyInRam(memoryAsmWriter);
        memoryAsmWriter.CurrentAddress = savedAddress;
        return CallbackLocation;
    }

    /// <summary>
    /// Gets the BIOS keyboard buffer.
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    /// <inheritdoc />
    public override void Run() {
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.DisablePortKbd);
        byte scanCode = _keyboard.ReadByte(KeyboardPorts.Data);
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.EnablePortKbd);

        byte savedAl = State.AL;
        State.AL = scanCode;
        bool savedCarryFlag = State.CarryFlag;
        byte savedAh = State.AH;
        // we first call INT15H AH=4H for keyboard intercept.
        State.AH = 0x4F;
        _emulationLoopRecall.RunInterrupt(0x15);
        bool shouldBeProcessed = State.CarryFlag;
        scanCode = State.AL;
        State.AH = savedAh;
        State.AL = savedAl;
        State.CarryFlag = savedCarryFlag;

        if(!shouldBeProcessed) {
            return;
        }

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 9 processing scan code: 0x{ScanCode:X2} after INT 15h", scanCode);
        }

        byte? ascii = _scanCodeConverter.GetAsciiCode(scanCode);
        
        ushort keyCode = (ushort)((scanCode << 8) | (ascii ?? 0));
        BiosKeyboardBuffer.EnqueueKeyCode(keyCode);
        _dualPic.AcknowledgeInterrupt(1);
    }
}