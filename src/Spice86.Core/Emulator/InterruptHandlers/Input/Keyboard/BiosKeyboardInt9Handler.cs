namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Collections.Frozen;

/// <summary>
/// Crude implementation of BIOS keyboard buffer handler (hardware interrupt 0x9, IRQ1)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Keyboard _keyboard;
    private readonly DualPic _dualPic;

    /// <summary>
    /// A dictionary that maps keyboard scan codes to their corresponding ASCII codes.
    /// </summary>
    private static readonly FrozenDictionary<byte, byte> _scanCodeToAscii;

    static BiosKeyboardInt9Handler() {
        _scanCodeToAscii = new Dictionary<byte, byte>()
            {
            {0x01, 0x1B},
            {0x02, 0x31},
            {0x03, 0x32},
            {0x04, 0x33},
            {0x05, 0x34},
            {0x06, 0x35},
            {0x07, 0x36},
            {0x08, 0x37},
            {0x09, 0x38},
            {0x0A, 0x39},
            {0x0B, 0x30},
            {0x0C, 0x2D},
            {0x0D, 0x3D},
            {0x0E, 0x08},
            {0x0F, 0x09},
            {0x10, 0x71},
            {0x11, 0x77},
            {0x12, 0x65},
            {0x13, 0x72},
            {0x14, 0x74},
            {0x15, 0x79},
            {0x16, 0x75},
            {0x17, 0x69},
            {0x18, 0x6F},
            {0x19, 0x70},
            {0x1A, 0x5B},
            {0x1B, 0x5D},
            {0x1C, 0x0D},
            {0x1E, 0x61},
            {0x1F, 0x73},
            {0x20, 0x64},
            {0x21, 0x66},
            {0x22, 0x67},
            {0x23, 0x68},
            {0x24, 0x6A},
            {0x25, 0x6B},
            {0x26, 0x6C},
            {0x27, 0x3B},
            {0x28, 0x27},
            {0x29, 0x60},
            {0x2B, 0x5C},
            {0x2C, 0x7A},
            {0x2D, 0x78},
            {0x2E, 0x63},
            {0x2F, 0x76},
            {0x30, 0x62},
            {0x31, 0x6E},
            {0x32, 0x6D},
            {0x33, 0x2C},
            {0x34, 0x2E},
            {0x35, 0x2F},
            {0x37, 0x2A},
            {0x39, 0x20},
            {0x4A, 0x2D},
            {0x4C, 0x35},
            {0x4E, 0x2B},
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="keyboard">The keyboard controller.</param>
    /// <param name="biosKeyboardBuffer">The structure in emulated memory this interrupt handler writes to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider, Stack stack,
        State state, DualPic dualPic, Keyboard keyboard,
        BiosKeyboardBuffer biosKeyboardBuffer, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _keyboard = keyboard;
        _dualPic = dualPic;
        BiosKeyboardBuffer = biosKeyboardBuffer;
    }

    /// <summary>
    /// Gets the ASCII code from the input scancode.
    /// </summary>
    /// <param name="scanCode">The scancode of the pressed keyboard key</param>
    /// <returns>The corresponding ASCII code, or <c>null</c> if not found.</returns>
    public byte GetAsciiCode(byte scanCode) {
        // Only convert to ASCII for key presses (not releases)
        if ((scanCode & 0x80) != 0) {
            return 0;
        }
        
        if (scanCode is not 0 && _scanCodeToAscii.TryGetValue(scanCode, out byte value)) {
            return value;
        }

        return 0;
    }

    /// <summary>
    /// Gets the BIOS keyboard buffer.
    /// </summary>
    public BiosKeyboardBuffer BiosKeyboardBuffer { get; }

    /// <inheritdoc />
    public override byte VectorNumber => 0x9;

    /// <inheritdoc />
    public override void Run() {
        byte scanCode = 0;
        
        byte statusByte = _keyboard.ReadByte(KeyboardPorts.StatusRegister);
        bool dataAvailable = (statusByte & Keyboard.OutputBufferFullMask) != 0;
        
        if (dataAvailable) {
            scanCode = _keyboard.DequeueEvent();
            
            if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
                LoggerService.Verbose("{BiosInt9KeyReceived} ScanCode: {ScanCode:X2}", nameof(Run), scanCode);
            }
            byte ascii = GetAsciiCode(scanCode);
            ushort keyCode = (ushort)((scanCode << 8) | ascii);
            BiosKeyboardBuffer.EnqueueKeyCode(keyCode);
        } else {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("INT9 triggered but no keyboard data available");
            }
        }
        _dualPic.AcknowledgeInterrupt(1);
    }
}