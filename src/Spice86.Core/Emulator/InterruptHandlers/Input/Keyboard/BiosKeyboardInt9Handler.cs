namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
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

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="keyboard">The keyboard device for direct port access.</param>
    /// <param name="biosKeyboardBuffer">The structure in emulated memory this interrupt handler writes to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory, Stack stack, State state,
        IFunctionHandlerProvider functionHandlerProvider, DualPic dualPic,
        PS2Keyboard keyboard, BiosKeyboardBuffer biosKeyboardBuffer,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        BiosKeyboardBuffer = biosKeyboardBuffer;
        _keyboard = keyboard;
        _dualPic = dualPic;
    }

    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress savedAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = CallbackLocation;

        // push ax (save original AX)
        memoryAsmWriter.WriteUInt8(0x50);
        
        // Disable keyboard port
        // mov al, 0xad
        memoryAsmWriter.WriteUInt8(0xB0);
        memoryAsmWriter.WriteUInt8(0xAD);
        // out 0x64, al
        memoryAsmWriter.WriteUInt8(0xE6);
        memoryAsmWriter.WriteUInt8(0x64);

        // Handle keyboard interception via INT 15h
        // mov ah, 0x4f
        memoryAsmWriter.WriteUInt8(0xB4);
        memoryAsmWriter.WriteUInt8(0x4F);
        // stc (set carry flag)
        memoryAsmWriter.WriteUInt8(0xF9);
        // int 0x15
        memoryAsmWriter.WriteUInt8(0xCD);
        memoryAsmWriter.WriteUInt8(0x15);
        
        // jnc done (skip processing if carry flag is not set)
        memoryAsmWriter.WriteUInt8(0x73);
        memoryAsmWriter.WriteUInt8(0x04);
        
        // Call our C# callback (only if carry flag is set)
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
        
        // done:
        // Re-enable keyboard port
        // mov al, 0xae
        memoryAsmWriter.WriteUInt8(0xB0);
        memoryAsmWriter.WriteUInt8(0xAE);
        // out 0x64, al
        memoryAsmWriter.WriteUInt8(0xE6);
        memoryAsmWriter.WriteUInt8(0x64);

        // Process the key, handle PIC
        // cli
        memoryAsmWriter.WriteUInt8(0xFA);
        // mov al, 0x20 (EOI command)
        memoryAsmWriter.WriteUInt8(0xB0);
        memoryAsmWriter.WriteUInt8(0x20);
        // out 0x20, al (send EOI to master PIC)
        memoryAsmWriter.WriteUInt8(0xE6);
        memoryAsmWriter.WriteUInt8(0x20);
        // pop ax (restore original AX value)
        memoryAsmWriter.WriteUInt8(0x58);
        memoryAsmWriter.WriteIret();

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
        // The scancode is in AL - INT 15h might have modified it
        byte scanCode = _keyboard.ReadByte(KeyboardPorts.Data);
        
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 9 processing scan code: 0x{ScanCode:X2} after INT 15h", scanCode);
        }

        // Convert scan code to ASCII using our helper
        byte? ascii = _scanCodeConverter.GetAsciiCode(scanCode);
        
        // Enqueue the key code into the BIOS keyboard buffer
        ushort keyCode = (ushort)((scanCode << 8) | (ascii ?? 0));
        BiosKeyboardBuffer.EnqueueKeyCode(keyCode);
        _dualPic.AcknowledgeInterrupt(1);
    }
}