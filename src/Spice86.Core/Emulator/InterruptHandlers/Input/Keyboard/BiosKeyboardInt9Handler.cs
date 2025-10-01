namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Serilog.Events;
using System.Collections.Frozen;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of BIOS keyboard buffer handler (hardware interrupt 0x9, IRQ1)
/// </summary>
public class BiosKeyboardInt9Handler : InterruptHandler {
    private readonly Intel8042Controller _keyboard;
    private readonly SystemBiosInt15Handler _systemBiosInt15Handler;
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

        // flush buffer if overflowed
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.ReadByteConfig);

        // enable keyboard again
        _keyboard.WriteByte(KeyboardPorts.Command, (byte)KeyboardCommand.EnablePortKbd);

        // PIC EOI
        _dualPic.AcknowledgeInterrupt(1);
    }

    /// <summary>
    /// Direct port of DOSBox's KeyCodes structure for keyboard scan code mappings
    /// </summary>
    public record KeyCodes {
        /// <summary>
        /// Normal key mapping
        /// </summary>
        public ushort Normal { get; init; }
        
        /// <summary>
        /// Shifted key mapping
        /// </summary>
        public ushort Shift { get; init; }
        
        /// <summary>
        /// Control+key mapping
        /// </summary>
        public ushort Control { get; init; }

        /// <summary>
        /// Alt+key mapping
        /// </summary>
        public ushort Alt { get; init; }

        /// <summary>
        /// Initializes a new instance with all key mappings
        /// </summary>
        public KeyCodes(ushort normal, ushort shift, ushort control, ushort alt) {
            Normal = normal;
            Shift = shift;
            Control = control;
            Alt = alt;
        }
    }

    /// <summary>
    /// Provides keyboard scan code to key code mappings, direct port of DOSBox's get_key_codes_for function
    /// </summary>
    public static class KeyboardMap {
        private const ushort None = 0;
        
        private static readonly FrozenDictionary<byte, KeyCodes> _keyboardCodes;

        static KeyboardMap() {
            var dict = new Dictionary<byte, KeyCodes>(116) {
                [0] = new KeyCodes(None, None, None, None),
                [1] = new KeyCodes(0x011b, 0x011b, 0x011b, 0x01f0), /* escape */
                [2] = new KeyCodes(0x0231, 0x0221, None, 0x7800), /* 1! */
                [3] = new KeyCodes(0x0332, 0x0340, 0x0300, 0x7900), /* 2@ */
                [4] = new KeyCodes(0x0433, 0x0423, None, 0x7a00), /* 3# */
                [5] = new KeyCodes(0x0534, 0x0524, None, 0x7b00), /* 4$ */
                [6] = new KeyCodes(0x0635, 0x0625, None, 0x7c00), /* 5% */
                [7] = new KeyCodes(0x0736, 0x075e, 0x071e, 0x7d00), /* 6^ */
                [8] = new KeyCodes(0x0837, 0x0826, None, 0x7e00), /* 7& */
                [9] = new KeyCodes(0x0938, 0x092a, None, 0x7f00), /* 8* */
                [10] = new KeyCodes(0x0a39, 0x0a28, None, 0x8000), /* 9( */
                [11] = new KeyCodes(0x0b30, 0x0b29, None, 0x8100), /* 0) */
                [12] = new KeyCodes(0x0c2d, 0x0c5f, 0x0c1f, 0x8200), /* -_ */
                [13] = new KeyCodes(0x0d3d, 0x0d2b, None, 0x8300), /* =+ */
                [14] = new KeyCodes(0x0e08, 0x0e08, 0x0e7f, 0x0ef0), /* backspace */
                [15] = new KeyCodes(0x0f09, 0x0f00, 0x9400, None), /* tab */
                [16] = new KeyCodes(0x1071, 0x1051, 0x1011, 0x1000), /* Q */
                [17] = new KeyCodes(0x1177, 0x1157, 0x1117, 0x1100), /* W */
                [18] = new KeyCodes(0x1265, 0x1245, 0x1205, 0x1200), /* E */
                [19] = new KeyCodes(0x1372, 0x1352, 0x1312, 0x1300), /* R */
                [20] = new KeyCodes(0x1474, 0x1454, 0x1414, 0x1400), /* T */
                [21] = new KeyCodes(0x1579, 0x1559, 0x1519, 0x1500), /* Y */
                [22] = new KeyCodes(0x1675, 0x1655, 0x1615, 0x1600), /* U */
                [23] = new KeyCodes(0x1769, 0x1749, 0x1709, 0x1700), /* I */
                [24] = new KeyCodes(0x186f, 0x184f, 0x180f, 0x1800), /* O */
                [25] = new KeyCodes(0x1970, 0x1950, 0x1910, 0x1900), /* P */
                [26] = new KeyCodes(0x1a5b, 0x1a7b, 0x1a1b, 0x1af0), /* [{ */
                [27] = new KeyCodes(0x1b5d, 0x1b7d, 0x1b1d, 0x1bf0), /* ]} */
                [28] = new KeyCodes(0x1c0d, 0x1c0d, 0x1c0a, None), /* Enter */
                [29] = new KeyCodes(None, None, None, None), /* L Ctrl */
                [30] = new KeyCodes(0x1e61, 0x1e41, 0x1e01, 0x1e00), /* A */
                [31] = new KeyCodes(0x1f73, 0x1f53, 0x1f13, 0x1f00), /* S */
                [32] = new KeyCodes(0x2064, 0x2044, 0x2004, 0x2000), /* D */
                [33] = new KeyCodes(0x2166, 0x2146, 0x2106, 0x2100), /* F */
                [34] = new KeyCodes(0x2267, 0x2247, 0x2207, 0x2200), /* G */
                [35] = new KeyCodes(0x2368, 0x2348, 0x2308, 0x2300), /* H */
                [36] = new KeyCodes(0x246a, 0x244a, 0x240a, 0x2400), /* J */
                [37] = new KeyCodes(0x256b, 0x254b, 0x250b, 0x2500), /* K */
                [38] = new KeyCodes(0x266c, 0x264c, 0x260c, 0x2600), /* L */
                [39] = new KeyCodes(0x273b, 0x273a, None, 0x27f0), /* ;: */
                [40] = new KeyCodes(0x2827, 0x2822, None, 0x28f0), /* '" */
                [41] = new KeyCodes(0x2960, 0x297e, None, 0x29f0), /* `~ */
                [42] = new KeyCodes(None, None, None, None), /* L shift */
                [43] = new KeyCodes(0x2b5c, 0x2b7c, 0x2b1c, 0x2bf0), /* |\ */
                [44] = new KeyCodes(0x2c7a, 0x2c5a, 0x2c1a, 0x2c00), /* Z */
                [45] = new KeyCodes(0x2d78, 0x2d58, 0x2d18, 0x2d00), /* X */
                [46] = new KeyCodes(0x2e63, 0x2e43, 0x2e03, 0x2e00), /* C */
                [47] = new KeyCodes(0x2f76, 0x2f56, 0x2f16, 0x2f00), /* V */
                [48] = new KeyCodes(0x3062, 0x3042, 0x3002, 0x3000), /* B */
                [49] = new KeyCodes(0x316e, 0x314e, 0x310e, 0x3100), /* N */
                [50] = new KeyCodes(0x326d, 0x324d, 0x320d, 0x3200), /* M */
                [51] = new KeyCodes(0x332c, 0x333c, None, 0x33f0), /* ,< */
                [52] = new KeyCodes(0x342e, 0x343e, None, 0x34f0), /* .> */
                [53] = new KeyCodes(0x352f, 0x353f, None, 0x35f0), /* /? */
                [54] = new KeyCodes(None, None, None, None), /* R Shift */
                [55] = new KeyCodes(0x372a, 0x372a, 0x9600, 0x37f0), /* * */
                [56] = new KeyCodes(None, None, None, None), /* L Alt */
                [57] = new KeyCodes(0x3920, 0x3920, 0x3920, 0x3920), /* space */
                [58] = new KeyCodes(None, None, None, None), /* caps lock */
                [59] = new KeyCodes(0x3b00, 0x5400, 0x5e00, 0x6800), /* F1 */
                [60] = new KeyCodes(0x3c00, 0x5500, 0x5f00, 0x6900), /* F2 */
                [61] = new KeyCodes(0x3d00, 0x5600, 0x6000, 0x6a00), /* F3 */
                [62] = new KeyCodes(0x3e00, 0x5700, 0x6100, 0x6b00), /* F4 */
                [63] = new KeyCodes(0x3f00, 0x5800, 0x6200, 0x6c00), /* F5 */
                [64] = new KeyCodes(0x4000, 0x5900, 0x6300, 0x6d00), /* F6 */
                [65] = new KeyCodes(0x4100, 0x5a00, 0x6400, 0x6e00), /* F7 */
                [66] = new KeyCodes(0x4200, 0x5b00, 0x6500, 0x6f00), /* F8 */
                [67] = new KeyCodes(0x4300, 0x5c00, 0x6600, 0x7000), /* F9 */
                [68] = new KeyCodes(0x4400, 0x5d00, 0x6700, 0x7100), /* F10 */
                [69] = new KeyCodes(None, None, None, None), /* Num Lock */
                [70] = new KeyCodes(None, None, None, None), /* Scroll Lock */
                [71] = new KeyCodes(0x4700, 0x4737, 0x7700, 0x0007), /* 7 Home */
                [72] = new KeyCodes(0x4800, 0x4838, 0x8d00, 0x0008), /* 8 UP */
                [73] = new KeyCodes(0x4900, 0x4939, 0x8400, 0x0009), /* 9 PgUp */
                [74] = new KeyCodes(0x4a2d, 0x4a2d, 0x8e00, 0x4af0), /* - */
                [75] = new KeyCodes(0x4b00, 0x4b34, 0x7300, 0x0004), /* 4 Left */
                [76] = new KeyCodes(0x4cf0, 0x4c35, 0x8f00, 0x0005), /* 5 */
                [77] = new KeyCodes(0x4d00, 0x4d36, 0x7400, 0x0006), /* 6 Right */
                [78] = new KeyCodes(0x4e2b, 0x4e2b, 0x9000, 0x4ef0), /* + */
                [79] = new KeyCodes(0x4f00, 0x4f31, 0x7500, 0x0001), /* 1 End */
                [80] = new KeyCodes(0x5000, 0x5032, 0x9100, 0x0002), /* 2 Down */
                [81] = new KeyCodes(0x5100, 0x5133, 0x7600, 0x0003), /* 3 PgDn */
                [82] = new KeyCodes(0x5200, 0x5230, 0x9200, 0x0000), /* 0 Ins */
                [83] = new KeyCodes(0x5300, 0x532e, 0x9300, None), /* Del */
                [84] = new KeyCodes(None, None, None, None), /* SysRq */
                [85] = new KeyCodes(None, None, None, None),
                [86] = new KeyCodes(0x565c, 0x567c, None, None), /* OEM102 */
                [87] = new KeyCodes(0x8500, 0x8700, 0x8900, 0x8b00), /* F11 */
                [88] = new KeyCodes(0x8600, 0x8800, 0x8a00, 0x8c00), /* F12 */
                [89] = new KeyCodes(None, None, None, None),
                [90] = new KeyCodes(None, None, None, None),
                [91] = new KeyCodes(None, None, None, None), /* Win Left */
                [92] = new KeyCodes(None, None, None, None), /* Win Right */
                [93] = new KeyCodes(None, None, None, None), /* Win Menu */
                [94] = new KeyCodes(None, None, None, None),
                [95] = new KeyCodes(None, None, None, None),
                [96] = new KeyCodes(None, None, None, None),
                [97] = new KeyCodes(None, None, None, None),
                [98] = new KeyCodes(None, None, None, None),
                [99] = new KeyCodes(None, None, None, None), /* F16 */
                [100] = new KeyCodes(None, None, None, None), /* F17 */
                [101] = new KeyCodes(None, None, None, None), /* F18 */
                [102] = new KeyCodes(None, None, None, None), /* F19 */
                [103] = new KeyCodes(None, None, None, None), /* F20 */
                [104] = new KeyCodes(None, None, None, None), /* F21 */
                [105] = new KeyCodes(None, None, None, None), /* F22 */
                [106] = new KeyCodes(None, None, None, None), /* F23 */
                [107] = new KeyCodes(None, None, None, None), /* F24 */
                [108] = new KeyCodes(None, None, None, None),
                [109] = new KeyCodes(None, None, None, None),
                [110] = new KeyCodes(None, None, None, None),
                [111] = new KeyCodes(None, None, None, None),
                [112] = new KeyCodes(None, None, None, None),
                [113] = new KeyCodes(None, None, None, None), /* Attn */
                [114] = new KeyCodes(None, None, None, None), /* CrSel */
                [115] = new KeyCodes(0x7330, 0x7340, None, 0x73f0) /* /? ABNT1 or ABNT_C1 */
            };

            _keyboardCodes = dict.ToFrozenDictionary();
        }

        /// <summary>
        /// Gets the key codes for a specific scan code - direct port of DOSBox's get_key_codes_for function
        /// </summary>
        /// <param name="scanCode">The keyboard scan code</param>
        /// <returns>The key codes structure for the scan code</returns>
        public static KeyCodes GetKeyCodesFor(byte scanCode) {
            if (_keyboardCodes.TryGetValue(scanCode, out KeyCodes? codes)) {
                return codes;
            }
            return new KeyCodes(None, None, None, None);
        }
    }

    private void ProcessScancode(byte scanCode, ref byte flags1, ref byte flags2, ref byte flags3, ref byte leds) {
        switch (scanCode) {
        /* First the hard ones  */
        case 0xfa:	/* Acknowledge */
            leds |= 0x10;
            break;
        case 0xe1:	/* Extended key special. Only pause uses this */
            flags3 |= 0x01;
            break;
        case 0xe0:						/* Extended key */
            flags3 |= 0x02;
            break;
        case 0x1d:						/* Ctrl Pressed */
            if ((flags3 & 0x01) == 0) {
                flags1 |= 0x04;
                if ((flags3 & 0x02) != 0) flags3 |= 0x04;
                else flags2 |= 0x01;
            }	/* else it's part of the pause scancodes */
            break;
        case 0x9d:						/* Ctrl Released */
            if ((flags3 & 0x01) == 0) {
                if ((flags3 & 0x02) != 0) flags3 = (byte)(flags3 & ~0x04);
                else flags2 = (byte)(flags2 & ~0x01);
                if (!((flags3 & 0x04) != 0 || (flags2 & 0x01) != 0)) flags1 = (byte)(flags1 & ~0x04);
            }
            break;
        case 0x2a:						/* Left Shift Pressed */
            flags1 |= 0x02;
            break;
        case 0xaa:						/* Left Shift Released */
            flags1 = (byte)(flags1 & ~0x02);
            break;
        case 0x36:						/* Right Shift Pressed */
            flags1 |= 0x01;
            break;
        case 0xb6:						/* Right Shift Released */
            flags1 = (byte)(flags1 & ~0x01);
            break;
        case 0x37:						/* Keypad * or PrtSc Pressed */
            if ((flags3 & 0x02) == 0) goto normal_key;
            // TODO: Not implemented -> call INT 0x5
            break;
        case 0xb7:						/* Keypad * or PrtSc Released */
            if ((flags3 & 0x02) == 0) goto normal_key;
            break;
        case 0x38:						/* Alt Pressed */
            flags1 |= 0x08;
            if ((flags3 & 0x02) != 0) flags3 |= 0x08;
            else flags2 |= 0x02;
            break;
        case 0xb8:						/* Alt Released */
            if ((flags3 & 0x02) != 0) flags3 = (byte)(flags3 & ~0x08);
            else flags2 = (byte)(flags2 & ~0x02);
            if (!((flags3 & 0x08) != 0 || (flags2 & 0x02) != 0)) { /* Both alt released */
                flags1 = (byte)(flags1 & ~0x08);
                byte token = BiosKeyboardBuffer.BiosDataArea.AltKeypad;
                if (token != 0) {
                    BiosKeyboardBuffer.EnqueueKeyCode(token);
                    BiosKeyboardBuffer.BiosDataArea.AltKeypad = 0;
                }
            }
            break;
        case 0x3a: flags2 |= 0x40; break;//CAPSLOCK
        case 0xba: flags1 ^= 0x40; flags2 = (byte)(flags2 & ~0x40); leds ^= 0x04; break;
        case 0x45:
            if ((flags3 & 0x01) != 0) {
                /* last scancode of pause received; first remove 0xe1-prefix */
                flags3 = (byte)(flags3 & ~0x01);
                BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag3 = flags3;
                if ((flags2 & 1) != 0) {
                    /* Ctrl+Pause (Break), special handling needed:
                       add zero to the keyboard buffer, call int 0x1b which
                       sets Ctrl+C flag which calls int 0x23 in certain dos
                       input/output functions;    not implemented */
                } else if ((flags2 & 8) == 0) {
                    /* normal pause key */
                    BiosKeyboardBuffer.BiosDataArea.KeyboardStatusFlag2 = (byte)(flags2 | 8);
                    // busy loop until Pause is used again is not implemented
                    return;
                }
            } else {
                /* Num Lock */
                flags2 |= 0x20;
            }
            break;
        case 0xc5:
            if ((flags3 & 0x01) != 0) {
                /* pause released */
                flags3 = (byte)(flags3 & ~0x01);
            } else {
                flags1 ^= 0x20;
                leds ^= 0x02;
                flags2 = (byte)(flags2 & ~0x20);
            }
            break;
        case 0x46: flags2 |= 0x10; break;				/* Scroll Lock */
        case 0xc6: flags1 ^= 0x10; flags2 = (byte)(flags2 & ~0x10); leds ^= 0x01; break;
        //case 0x52:flags2|=128;break;//See numpad					/* Insert */
        case 0xd2:	
            if ((flags3 & 0x02) != 0) { /* Maybe honour the insert on keypad as well */
                flags1 ^= 0x80;
                flags2 = (byte)(flags2 & ~0x80);
                break; 
            } else {
                goto irq1_end; /*Normal release*/ 
            }
        case 0x47:		/* Numpad */
        case 0x48:
        case 0x49:
        case 0x4b:
        case 0x4c:
        case 0x4d:
        case 0x4f:
        case 0x50:
        case 0x51:
        case 0x52:
        case 0x53: /* del . Not entirely correct, but works fine */
            if ((flags3 & 0x02) != 0) {	/*extend key. e.g key above arrows or arrows*/
                if (scanCode == 0x52) flags2 |= 0x80; /* press insert */		   
                if ((flags1 & 0x08) != 0) {
                    BiosKeyboardBuffer.EnqueueKeyCode((ushort)(KeyboardMap.GetKeyCodesFor(scanCode).Normal + 0x5000));
                } else if ((flags1 & 0x04) != 0) {
                    BiosKeyboardBuffer.EnqueueKeyCode((ushort)((KeyboardMap.GetKeyCodesFor(scanCode).Control & 0xff00) | 0xe0));
                } else if (((flags1 & 0x3) != 0) || ((flags1 & 0x20) != 0)) {
                    // Due to |0xe0 results are identical
                    BiosKeyboardBuffer.EnqueueKeyCode((ushort)((KeyboardMap.GetKeyCodesFor(scanCode).Shift & 0xff00) | 0xe0));
                } else {
                    BiosKeyboardBuffer.EnqueueKeyCode((ushort)((KeyboardMap.GetKeyCodesFor(scanCode).Normal & 0xff00) | 0xe0));
                }
                break;
            }
            if ((flags1 & 0x08) != 0) {
                byte token = BiosKeyboardBuffer.BiosDataArea.AltKeypad;
                ushort alt = KeyboardMap.GetKeyCodesFor(scanCode).Alt;
                byte combined = (byte)((token * 10 + alt) & 0xFF);
                BiosKeyboardBuffer.BiosDataArea.AltKeypad = combined;
            } else if ((flags1 & 0x04) != 0) {
                BiosKeyboardBuffer.EnqueueKeyCode(KeyboardMap.GetKeyCodesFor(scanCode).Control);
            } else if (((flags1 & 0x3) != 0) ^ ((flags1 & 0x20) != 0)) {
                // Xor shift and numlock (both means off)
                BiosKeyboardBuffer.EnqueueKeyCode(KeyboardMap.GetKeyCodesFor(scanCode).Shift);
            } else {
                BiosKeyboardBuffer.EnqueueKeyCode(KeyboardMap.GetKeyCodesFor(scanCode).Normal);
            }
            break;

        default: /* Normal Key */
normal_key:
            ushort asciiscan;
            /* Now Handle the releasing of keys and see if they match up for a code */
            /* Handle the actual scancode */
            if ((scanCode & 0x80) != 0) goto irq1_end;
            if (scanCode > 115) goto irq1_end;
            if ((flags1 & 0x08) != 0) { 					/* Alt is being pressed */
                asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Alt;
            } else if ((flags1 & 0x04) != 0) {					/* Ctrl is being pressed */
                asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Control;
            } else if ((flags1 & 0x03) != 0) {					/* Either shift is being pressed */
                asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Shift;
            } else {
                asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Normal;
            }
            /* cancel shift is letter and capslock active */
            if ((flags1 & 64) != 0) {
                if ((flags1 & 3) != 0) {
                    /*cancel shift */
                    if (((asciiscan & 0x00ff) > 0x40) && ((asciiscan & 0x00ff) < 0x5b)) {
                        asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Normal;
                    }
                } else {
                    /* add shift */
                    if (((asciiscan & 0x00ff) > 0x60) && ((asciiscan & 0x00ff) < 0x7b)) {
                        asciiscan = KeyboardMap.GetKeyCodesFor(scanCode).Shift;
                    }
                }
            }
            if ((flags3 & 0x02) != 0) {
                /* extended key (numblock), return and slash need special handling */
                if (scanCode == 0x1c) {	/* return */
                    if ((flags1 & 0x08) != 0) asciiscan = 0xa600;
                    else asciiscan = (ushort)((asciiscan & 0xff) | 0xe000);
                } else if (scanCode == 0x35) {	/* slash */
                    if ((flags1 & 0x08) != 0) asciiscan = 0xa400;
                    else if ((flags1 & 0x04) != 0) asciiscan = 0x9500;
                    else asciiscan = 0xe02f;
                }
            }
            BiosKeyboardBuffer.EnqueueKeyCode(asciiscan);
            break;
        };
irq1_end:
        if (scanCode != 0xe0) flags3 = (byte)(flags3 & ~0x02);                                    //Reset 0xE0 Flag
        if ((scanCode & 0x80) == 0) flags2 &= 0xf7;
    }
}