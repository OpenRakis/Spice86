namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of BIOS keyboard buffer handler (hardware interrupt 0x9, IRQ1)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Intel8042Controller _keyboard;
    private readonly SystemBiosInt15Handler _systemBiosInt15Handler;
    private static readonly SegmentedAddress CallbackLocation = new(0xf000, 0xe987);
    private readonly DualPic _dualPic;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="stack">The CPU stack.</param>
    /// <param name="state">The CPU state.</param>
    /// <param name="functionHandlerProvider">Provides current call flow handler to peek call stack.</param>
    /// <param name="dualPic">The interrupt controller, used to eventually acknowledge IRQ1 hardware interrupt.</param>
    /// <param name="systemBiosInt15Handler">INT15H BIOS handler used for the keyboard intercept function.</param>
    /// <param name="keyboard">The keyboard device for direct port access.</param>
    /// <param name="biosKeyboardBuffer">The structure in emulated memory this interrupt handler writes to.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public BiosKeyboardInt9Handler(IMemory memory, Stack stack, State state,
        IFunctionHandlerProvider functionHandlerProvider, DualPic dualPic,
        SystemBiosInt15Handler systemBiosInt15Handler, Intel8042Controller keyboard,
        BiosKeyboardBuffer biosKeyboardBuffer, ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        BiosKeyboardBuffer = biosKeyboardBuffer;
        _systemBiosInt15Handler = systemBiosInt15Handler;
        _keyboard = keyboard;
        _dualPic = dualPic;
    }

    public override SegmentedAddress WriteAssemblyInRam(MemoryAsmWriter memoryAsmWriter) {
        SegmentedAddress savedAddress = memoryAsmWriter.CurrentAddress;
        memoryAsmWriter.CurrentAddress = CallbackLocation;
        memoryAsmWriter.RegisterAndWriteCallback(VectorNumber, Run);
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
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.DisablePortKbd);
        byte scancode = _keyboard.ReadByte(KeyboardPorts.Data);
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.EnablePortKbd);
        _systemBiosInt15Handler.KeyboardIntercept(calledFromVm: true);

        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("INT 9 processing scan code: 0x{ScanCode:X2}", scancode);
        }

        byte flags1 = BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag;
        byte flags2 = BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag2;
        byte flags3 = BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag3;
        byte leds = BiosKeyboardBuffer.BiosDataArea.KeyboardLedStatus;

        ProcessScancode(scancode, ref flags1, ref flags2, ref flags3, ref leds);

        BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag = flags1;
        BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag2 = flags2;
        BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag3 = flags3;
        BiosKeyboardBuffer.BiosDataArea.KeyboardLedStatus = leds;

        // PIC EOI
        _dualPic.AcknowledgeInterrupt(1);
    }

    private void ProcessScancode(byte scanCode, ref byte flags1, ref byte flags2, ref byte flags3, ref byte leds) {
        KeyboardScancodeConverter converter = new KeyboardScancodeConverter();

        // Implementation matching DOSBox's IRQ1_Handler
        switch (scanCode) {
            case 0xFA: // Acknowledge
                leds |= 0x10;
                break;
            case 0xE1: // Extended key special (only Pause uses this)
                flags3 |= 0x01;
                break;
            case 0xE0: // Extended key
                flags3 |= 0x02;
                break;
            case 0x1D: // Ctrl Pressed
                if ((flags3 & 0x01) == 0) {
                    flags1 |= 0x04;
                    if ((flags3 & 0x02) != 0) flags3 |= 0x04; // Right Ctrl
                    else flags2 |= 0x01; // Left Ctrl
                }
                break;
            case 0x9D: // Ctrl Released
                if ((flags3 & 0x01) == 0) {
                    if ((flags3 & 0x02) != 0) flags3 = (byte)(flags3 & ~0x04); // Right Ctrl
                    else flags2 = (byte)(flags2 & ~0x01); // Left Ctrl
                    if (((flags3 & 0x04) == 0) && ((flags2 & 0x01) == 0)) flags1 = (byte)(flags1 & ~0x04);
                }
                break;
            case 0x2A: // Left Shift Pressed
                flags1 |= 0x02;
                break;
            case 0xAA: // Left Shift Released
                flags1 = (byte)(flags1 & ~0x02);
                break;
            case 0x36: // Right Shift Pressed
                flags1 |= 0x01;
                break;
            case 0xB6: // Right Shift Released
                flags1 = (byte)(flags1 & ~0x01);
                break;
            case 0x38: // Alt Pressed
                flags1 |= 0x08;
                if ((flags3 & 0x02) != 0) flags3 |= 0x08; // Right Alt
                else flags2 |= 0x02; // Left Alt
                break;
            case 0xB8: // Alt Released
                if ((flags3 & 0x02) != 0) flags3 = (byte)(flags3 & ~0x08); // Right Alt
                else flags2 = (byte)(flags2 & ~0x02); // Left Alt

                if (((flags3 & 0x08) == 0) && ((flags2 & 0x02) == 0)) {
                    flags1 = (byte)(flags1 & ~0x08);
                    // Handle Alt+Numpad key combinations
                    byte token = BiosKeyboardBuffer.BiosDataArea.AltKeypad;
                    if (token != 0) {
                        BiosKeyboardBuffer.EnqueueKeyCode(token);
                        BiosKeyboardBuffer.BiosDataArea.AltKeypad = 0;
                    }
                }
                break;
            case 0x3A: flags2 |= 0x40; break; // CAPSLOCK pressed
            case 0xBA: flags1 ^= 0x40; flags2 = (byte)(flags2 & ~0x40); leds ^= 0x04; break; // CAPSLOCK released
            case 0x45: // NumLock or Pause
                if ((flags3 & 0x01) != 0) {
                    // Pause key handling
                    flags3 = (byte)(flags3 & ~0x01);
                    if ((flags2 & 0x01) != 0) {
                        // Ctrl+Pause (Break) handling
                        // Not implemented yet
                    } else if ((flags2 & 0x08) == 0) {
                        // Normal pause key
                        flags2 |= 0x08;
                        // Implement pause state
                    }
                } else {
                    // NumLock
                    flags2 |= 0x20;
                }
                break;
            case 0xC5: // NumLock or Pause released
                if ((flags3 & 0x01) != 0) {
                    flags3 = (byte)(flags3 & ~0x01);
                } else {
                    flags1 ^= 0x20;
                    leds ^= 0x02;
                    flags2 = (byte)(flags2 & ~0x20);
                }
                break;
            case 0x46: flags2 |= 0x10; break; // SCROLL LOCK pressed
            case 0xC6: flags1 ^= 0x10; flags2 = (byte)(flags2 & ~0x10); leds ^= 0x01; break; // SCROLL LOCK released
            case 0xD2: // Insert released
                if ((flags3 & 0x02) != 0) {
                    flags1 ^= 0x80;
                    flags2 = (byte)(flags2 & ~0x80);
                    break;
                }
                goto default;

            // Keypad handling
            case 0x47:
            case 0x48:
            case 0x49:
            case 0x4B:
            case 0x4C:
            case 0x4D:
            case 0x4F:
            case 0x50:
            case 0x51:
            case 0x52:
            case 0x53:
                if ((flags3 & 0x02) != 0) { // Extended key
                    if (scanCode == 0x52) flags2 |= 0x80; // press insert

                    ushort code = 0;
                    if ((flags1 & 0x08) != 0) { // Alt
                        // Alt+Arrow/etc
                        byte value = converter.GetAsciiCode(scanCode, flags1);
                        code = (ushort)(0x5000 + value);
                    } else if ((flags1 & 0x04) != 0) { // Ctrl
                        // Form control code
                        code = (ushort)(((converter.GetAsciiCode(scanCode, (byte)(flags1 | 0x04)) & 0xFF00)) | 0xE0);
                    } else if (((flags1 & 0x03) != 0) || ((flags1 & 0x20) != 0)) {
                        // Shift or NumLock
                        code = (ushort)(((converter.GetAsciiCode(scanCode, (byte)(flags1 | 0x03)) & 0xFF00)) | 0xE0);
                    } else {
                        // Normal extended key
                        code = (ushort)(((converter.GetAsciiCode(scanCode, flags1) & 0xFF00)) | 0xE0);
                    }

                    BiosKeyboardBuffer.EnqueueKeyCode(code);
                } else {
                    // Regular keypad key
                    if ((flags1 & 0x08) != 0) { // Alt+keypad = character code
                        byte token = BiosKeyboardBuffer.BiosDataArea.AltKeypad;
                        byte alt = converter.GetAsciiCode(scanCode, (byte)(flags1 | 0x08));
                        byte combined = (byte)((token * 10 + alt) % 256);
                        BiosKeyboardBuffer.BiosDataArea.AltKeypad = combined;
                    } else {
                        // Get appropriate ASCII based on modifier state
                        ushort code;
                        if ((flags1 & 0x04) != 0) { // Ctrl
                            code = (ushort)(converter.GetAsciiCode(scanCode, (byte)(flags1 | 0x04)) | (scanCode << 8));
                        } else if (((flags1 & 0x03) != 0) ^ ((flags1 & 0x20) != 0)) {
                            // Shift XOR NumLock
                            code = (ushort)(converter.GetAsciiCode(scanCode, (byte)(flags1 | 0x03)) | (scanCode << 8));
                        } else {
                            // Normal
                            code = (ushort)(converter.GetAsciiCode(scanCode, flags1) | (scanCode << 8));
                        }
                        BiosKeyboardBuffer.EnqueueKeyCode(code);
                    }
                }
                break;

            default:
                // Normal key handling
                if ((scanCode & 0x80) != 0) {
                    // Key release - just update flags
                    break;
                }

                // Get appropriate ASCII/scancode combination
                byte ascii = converter.GetAsciiCode(scanCode, flags1);
                ushort keyCode = (ushort)((scanCode << 8) | ascii);
                BiosKeyboardBuffer.EnqueueKeyCode(keyCode);
                break;
        }
    }
}