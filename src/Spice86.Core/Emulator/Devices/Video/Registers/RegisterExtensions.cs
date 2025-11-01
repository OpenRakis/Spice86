namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.Enums;

public static class RegisterExtensions {
    public static string Explain(this GraphicsControllerRegister controllerRegister, byte value) {
        switch (controllerRegister) {
            case GraphicsControllerRegister.SetReset:
                return string.Format("Planes: {0}{1}{2}{3}",
                    (value & 0x01) == 0x01 ? "#" : ".",
                    (value & 0x02) == 0x02 ? "#" : ".",
                    (value & 0x04) == 0x04 ? "#" : ".",
                    (value & 0x08) == 0x08 ? "#" : "."
                );
            case GraphicsControllerRegister.EnableSetReset:
                return string.Format(
                    "[0]Plane 0: {0}, [1]Plane 1: {1}, [2]Plane 2: {2}, [3]Plane 3: {3}, [4-7]Reserved",
                    (value & 0x01) == 0x01 ? "On" : "Off",
                    (value & 0x02) == 0x02 ? "On" : "Off",
                    (value & 0x04) == 0x04 ? "On" : "Off",
                    (value & 0x08) == 0x08 ? "On" : "Off"
                );
            case GraphicsControllerRegister.ColorCompare:
                return string.Format("Color index: {0} 0x{0:X2}", value);
            case GraphicsControllerRegister.DataRotate:
                break;
            case GraphicsControllerRegister.ReadMapSelect:
                return $"Plane{value}";
            case GraphicsControllerRegister.GraphicsMode:
                return string.Format(
                    "[0-1]Write mode: {0}, [2]Reserved, [3]Read mode: {1}, [4]Odd/Even: {2}, [5]Shift register: {3}, [6]256 color shift: {4}, [7]Reserved",
                    value & 0x03,
                    (value & 0x08) == 0x08 ? "ColorCompare" : "ReadMapSelect",
                    (value & 0x10) == 0x10 ? "On" : "Off",
                    (value & 0x20) == 0x20 ? "Interleaved" : "Normal",
                    (value & 0x40) == 0x40 ? "On" : "Off"
                );
            case GraphicsControllerRegister.MiscellaneousGraphics:
                return string.Format(
                    "[0]Graphics mode: {0}, [1]Odd/Even: {1}, [2-3]Memory map: {2}, [4-7]Reserved",
                    (value & 0x01) == 0x01 ? "Graphics" : "Text",
                    (value & 0x02) == 0x02 ? "Switched" : "Normal",
                    (value & 0x0C) switch {
                        0x00 => "A0000-BFFFF",
                        0x04 => "A0000-AFFFF",
                        0x08 => "B0000-B7FFF",
                        0x0C => "B8000-BFFFF",
                        _ => throw new ArgumentOutOfRangeException()
                    });
            case GraphicsControllerRegister.ColorDontCare:
                return string.Format("Planes: {0}{1}{2}{3}",
                    (value & 0x01) == 0x01 ? "#" : ".",
                    (value & 0x02) == 0x02 ? "#" : ".",
                    (value & 0x04) == 0x04 ? "#" : ".",
                    (value & 0x08) == 0x08 ? "#" : "."
                );
            case GraphicsControllerRegister.BitMask:
                return Convert.ToString(value, 2).PadLeft(8, '0');
            default:
                throw new ArgumentOutOfRangeException(nameof(controllerRegister), controllerRegister, null);
        }
        return "(explanation not yet implemented)";
    }

    public static string Explain(this SequencerRegister register, byte value) {
        switch (register) {
            case SequencerRegister.Reset:
                break;
            case SequencerRegister.ClockingMode:
                break;
            case SequencerRegister.PlaneMask:
                return $"Planes: {((value & 0x01) == 0x01 ? "." : "#")}{((value & 0x02) == 0x02 ? "." : "#")}{((value & 0x04) == 0x04 ? "." : "#")}{((value & 0x08) == 0x08 ? "." : "#")}";
            case SequencerRegister.CharacterMapSelect:
                break;
            case SequencerRegister.MemoryMode:
                return string.Format(
                    "[0]Reserved, [1]Extended memory: {0}, [2]Odd/Even mode: {1}, [3]Chain 4 mode: {2} [4-7]Reserved",
                    (value & 0x02) == 0x02 ? "Enabled" : "Disabled",
                    (value & 0x04) == 0x04 ? "Disabled" : "Enabled",
                    (value & 0x08) == 0x08 ? "Chained" : "Unchained");
            default:
                throw new ArgumentOutOfRangeException(nameof(register), register, null);
        }
        return "(explanation not yet implemented)";
    }

    public static string Explain(this CrtControllerRegister register, byte value) {
        switch (register) {
            case CrtControllerRegister.HorizontalTotal:
                break;
            case CrtControllerRegister.HorizontalDisplayEnd:
                break;
            case CrtControllerRegister.HorizontalBlankingStart:
                break;
            case CrtControllerRegister.HorizontalBlankingEnd:
                break;
            case CrtControllerRegister.HorizontalRetraceStart:
                break;
            case CrtControllerRegister.HorizontalRetraceEnd:
                break;
            case CrtControllerRegister.VerticalTotal:
                break;
            case CrtControllerRegister.Overflow:
                return string.Format(
                    "[0,5]Vertical total upper: {0}, [1,6]Vertical display end upper: {1}, [2,7]Vertical sync start upper: {2}, [3]Vertical blanking start upper: {3}, [4]Line compare upper: {4}",
                    (byte)(((value & 0x01) << 1) | ((value >> 5) & 0x01)),
                    (byte)((((value >> 1) & 0x01) << 1) | ((value >> 6) & 0x01)),
                    (byte)((((value >> 2) & 0x01) << 1) | ((value >> 7) & 0x01)),
                    (byte)((value >> 3) & 0x01),
                    (byte)((value >> 4) & 0x01)
                );
            case CrtControllerRegister.PresetRowScan:
                break;
            case CrtControllerRegister.CharacterCellHeight:
                return string.Format(
                    "[0-4]Maximum scan line: {0}, [5]Start Vertical Blanking bit9: {1}, [6]Line Compare bit9: {2}, [7]Scan Doubling: {3}",
                    value & 0x1F,
                    (value & 0x20) == 0x20 ? "1" : "0",
                    (value & 0x40) == 0x40 ? "1" : "0",
                    (value & 0x80) == 0x80 ? "1" : "0"
                );
            case CrtControllerRegister.CursorStart:
                break;
            case CrtControllerRegister.CursorEnd:
                break;
            case CrtControllerRegister.StartAddressHigh:
                return "High byte of the start address of the display buffer";
            case CrtControllerRegister.StartAddressLow:
                return "Low byte of the start address of the display buffer";
            case CrtControllerRegister.CursorLocationHigh:
                break;
            case CrtControllerRegister.CursorLocationLow:
                break;
            case CrtControllerRegister.VerticalRetraceStart:
                break;
            case CrtControllerRegister.VerticalRetraceEnd:
                break;
            case CrtControllerRegister.VerticalDisplayEnd:
                return string.Format(
                    "[0-3]Vertical Sync End: {0}, [4]Clear Vertical Interrupt: {1}, [5]Disable Vertical Interrupt: {2}, [6]Refresh Cycle: {3}, [7]Write Protect: {4}",
                    value & 0x0F,
                    (value & 0x10) == 0x10 ? "Enabled" : "Disabled",
                    (value & 0x20) == 0x20 ? "Enabled" : "Disabled",
                    (value & 0x40) == 0x40 ? "5" : "3",
                    (value & 0x80) == 0x80 ? "Enabled" : "Disabled"
                );
            case CrtControllerRegister.Offset:
                break;
            case CrtControllerRegister.UnderlineLocation:
                return string.Format(
                    "[0-4]Underline location: {0}, [5]Count by 4: {1}, [6]DWord mode: {2}, [7]Reserved",
                    value & 0x1F,
                    (value & 0x20) == 0x20 ? "Enabled" : "Disabled",
                    (value & 0x40) == 0x40 ? "Enabled" : "Disabled"
                );
            case CrtControllerRegister.VerticalBlankingStart:
                break;
            case CrtControllerRegister.VerticalBlankingEnd:
                break;
            case CrtControllerRegister.CrtModeControl:
                return string.Format(
                    "[0]Compatibility mode: {0}, [1]Select row scan counter: {1}, [2]Horizontal retrace select: {2}, [3]Count by 2: {3}, [4]Reserved, [5]Address wrap: {4}, [6]Word/Byte: {5}, [7]CRT Syncs: {6}",
                    (value & 0x01) == 0x01 ? "On" : "Off",
                    (value & 0x02) == 0x02 ? "No substitution" : "Substitution",
                    (value & 0x04) == 0x04 ? "Double" : "Normal",
                    (value & 0x08) == 0x08 ? "Double" : "Normal",
                    (value & 0x20) == 0x20 ? "On" : "Off",
                    (value & 0x40) == 0x40 ? "Byte" : "Word",
                    (value & 0x80) == 0x80 ? "On" : "Off"
                );
            case CrtControllerRegister.LineCompare:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(register), register, null);
        }
        return "(explanation not yet implemented)";
    }
}

public static class MiscOutputRegister {
    public static string Explain(byte value) {
        return string.Format(" [0]I/OAS: {0}, [1]Ram en: {1}, [2-3]Clock Select: {2}, [4]Reserved, [5]O/E Page: {3}, [6]Hsync polarity: {4}, [7]Vsync Polarity: {5}",
            value & 0x01,
            (value & 0x02) == 0x02 ? "1" : "0",
            (value >> 3 & 3) switch {
                0 => "25Mhz",
                1 => "28Mhz",
                2 => "Reserved",
                3 => "Reserved",
                _ => "Unknown"
            },
            (value & 0x20) == 0x20 ? "High" : "Low",
            (value & 0x40) == 0x40 ? "1" : "0",
            (value & 0x80) == 0x80 ? "1" : "0"
        );
    }
}