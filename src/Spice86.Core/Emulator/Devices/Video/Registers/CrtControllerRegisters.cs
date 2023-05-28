namespace Spice86.Core.Emulator.Devices.Video.Registers;

using Spice86.Core.Emulator.Devices.Video.Registers.CrtController;
using Spice86.Core.Emulator.Devices.Video.Registers.Enums;

/// <summary>
///     Emulates the VGA CRT Controller registers.
/// </summary>
public sealed class CrtControllerRegisters {
    private int _screenStartAddress;
    private int _textCursorLocation;

    public CrtControllerRegister AddressRegister { get; set; }

    /// <summary>
    ///     This eight-bit field specifies the total number of character clocks per horizontal period. The Character
    ///     Clock (derived from the VCLK according to the character width) is counted in the Character Counter. The
    ///     value of the Character Counter is compared with the value in this register to provide the basic horizontal
    ///     timing. All horizontal and vertical timing is eventually derived from the register. The value in the
    ///     register is ‘Total number of character times minus five’.
    /// </summary>
    public byte HorizontalTotal { get; set; }

    /// <summary>
    ///     This register specifies the number of character clocks during horizontal display time. For text modes, this
    ///     is the number of characters; for graphics modes, this is the number of pixels per scan lines divided by the
    ///     number of pixels per character clock.The value in the register is the number of character clocks minus one.
    /// </summary>
    public byte HorizontalDisplayEnd { get; set; }

    /// <summary>
    ///     The contents of this register specify the Character Count where Horizontal Blanking starts. For text modes,
    ///     this is the number of characters; for graphics modes, this is the number of pixels per scanline divided by
    ///     the number of pixels per character clock. The value programmed into CR2 must always be larger than the
    ///     value programmed into CR1.
    /// </summary>
    public byte HorizontalBlankingStart { get; set; }

    /// <summary>
    ///     Gets or sets the End Horizontal Blanking register.
    /// </summary>
    public HorizontalBlankingEndRegister HorizontalBlankingEndRegister { get; } = new();

    /// <summary>
    ///     Gets the full 6-bit value of the Horizontal Blanking End register.
    /// </summary>
    public byte HorizontalBlankingEndValue => (byte)(HorizontalBlankingEndRegister.HorizontalBlankingEnd | HorizontalSyncEndRegister.HorizontalBlankingEnd5);

    /// <summary>
    ///     This field specifies the Character Count where HSYNC (Horizontal Sync) becomes active. Adjusting the value
    ///     in this field moves the display horizontally on the screen. The Horizontal Sync Start must be programmed to
    ///     a value equal to or greater than Horizontal Display End. The time from Horizontal Sync Start to Horizontal
    ///     Total must be equal to or greater than four character times.
    /// </summary>
    public byte HorizontalSyncStart { get; set; }

    /// <summary>
    ///     Gets or sets the End Horizontal Retrace register.
    /// </summary>
    public HorizontalSyncEndRegister HorizontalSyncEndRegister { get; set; } = new();

    /// <summary>
    ///     This field is the low-order eight bits of a ten-bit field that defines the total number of scan lines per
    ///     frame. This field is extended with CR5[7] and CR5[0]. The value programmed into the Vertical Total field
    ///     is the total number of scan lines minus two.
    /// </summary>
    public byte VerticalTotal { get; set; }

    /// <summary>
    ///     Gets the full 10-bit value of the Vertical Total register.
    /// </summary>
    public int VerticalTotalValue => VerticalTotal | OverflowRegister.VerticalTotal89;

    /// <summary>
    ///     Gets or sets the Overflow register.
    /// </summary>
    public OverflowRegister OverflowRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Preset Row Scan register.
    /// </summary>
    public PresetRowScanRegister PresetRowScanRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Maximum Scan Line register.
    /// </summary>
    public CharacterCellHeightRegister MaximumScanlineRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Cursor Start register.
    /// </summary>
    public TextCursorStartRegister TextCursorStartRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Cursor End register.
    /// </summary>
    public TextCursorEndRegister TextCursorEndRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Start Address High register.
    /// </summary>
    public byte ScreenStartAddressHigh {
        get => (byte)(_screenStartAddress >> 8);
        set => _screenStartAddress = _screenStartAddress & 0xFF | value << 8;
    }

    /// <summary>
    ///     Gets or sets the Start Address Low register.
    /// </summary>
    public byte ScreenStartAddressLow {
        get => (byte)(_screenStartAddress & 0xFF);
        set => _screenStartAddress = _screenStartAddress & 0xFF00 | value;
    }

    /// <summary>
    ///     Gets or sets the Start Address of video memory to be displayed.
    /// </summary>
    public int ScreenStartAddress {
        get => _screenStartAddress;
        set {
            ScreenStartAddressHigh = (byte)(value >> 8 & 0xFF);
            ScreenStartAddressLow = (byte)(value & 0xFF);
        }
    }

    /// <summary>
    ///     Gets or sets the Text Cursor Location High register.
    /// </summary>
    public byte TextCursorLocationHigh {
        get => (byte)(_textCursorLocation >> 8);
        set => _textCursorLocation = _textCursorLocation & 0xFF | value << 8;
    }

    /// <summary>
    ///     Gets or sets the Text Cursor Location Low register.
    /// </summary>
    public byte TextCursorLocationLow {
        get => (byte)(_screenStartAddress & 0xFF);
        set => _textCursorLocation = _textCursorLocation & 0xFF00 | value;
    }

    /// <summary>
    ///     Gets or sets the Address of the Text Cursor Location.
    /// </summary>
    public int TextCursorLocation {
        get => _textCursorLocation;
        set {
            TextCursorLocationHigh = (byte)(value >> 8 & 0xFF);
            TextCursorLocationLow = (byte)(value & 0xFF);
        }
    }

    /// <summary>
    ///     Gets or sets the Vertical Sync Start register.
    /// </summary>
    public byte VerticalSyncStart { get; set; }

    /// <summary>
    ///     Gets the full 10-bit value of the Vertical Sync Start register.
    /// </summary>
    public int VerticalSyncStartValue => VerticalSyncStart | OverflowRegister.VerticalSyncStart89;

    /// <summary>
    ///     Gets or sets the Vertical Sync End register.
    /// </summary>
    public VerticalSyncEndRegister VerticalSyncEndRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Vertical Display End register.
    /// </summary>
    public byte VerticalDisplayEnd { get; set; }

    /// <summary>
    ///     Gets the full 10-bit value of the Vertical Display End register.
    /// </summary>
    public int VerticalDisplayEndValue => VerticalDisplayEnd | OverflowRegister.VerticalDisplayEnd89;

    /// <summary>
    ///     This register specifies the distance in display memory between the beginnings of adjacent character rows or
    ///     scan lines. At the beginning of each scanline (except the first), the address where data fetching begins is
    ///     calculated by adding the contents of this register to the beginning address of the previous scanline or
    ///     character row. The offset is left-shifted one or two bit positions, depending on CR17[6].
    /// </summary>
    public byte Offset { get; set; }

    /// <summary>
    ///     Gets or sets the Underline Location register.
    /// </summary>
    public UnderlineRowScanlineRegister UnderlineRowScanlineRegister { get; set; } = new();

    /// <summary>
    ///     The Vertical Blank Start field specifies the scanline where Vertical Blank is to begin. The low-order eight
    ///     bits of that field are in this register.
    /// </summary>
    public byte VerticalBlankingStart { get; set; }

    /// <summary>
    ///     Gets the full 10-bit value of the Vertical Blanking Start register.
    /// </summary>
    public int VerticalBlankingStartValue => VerticalBlankingStart | OverflowRegister.VerticalBlankingStart8 | MaximumScanlineRegister.VerticalBlankStart9;

    /// <summary>
    ///     Gets or sets the End Vertical Blanking register.
    /// </summary>
    public byte VerticalBlankingEnd { get; set; }

    /// <summary>
    ///     Gets or sets the CRT Mode Control register.
    /// </summary>
    public CrtModeControlRegister CrtModeControlRegister { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Line Compare register.
    /// </summary>
    public byte LineCompare { get; set; }

    /// <summary>
    ///     Gets the full 10-bit value of the Line Compare register.
    /// </summary>
    public int LineCompareValue => LineCompare | OverflowRegister.LineCompare8 | MaximumScanlineRegister.LineCompare9;

    /// <summary>
    ///     Returns the current value of a CRT controller register.
    /// </summary>
    /// <param name="register">Address of register to read.</param>
    /// <returns>Current value of the register.</returns>
    public byte ReadRegister(CrtControllerRegister register) {
        return register switch {
            CrtControllerRegister.HorizontalTotal => HorizontalTotal,
            CrtControllerRegister.HorizontalDisplayEnd => HorizontalDisplayEnd,
            CrtControllerRegister.HorizontalBlankingStart => HorizontalBlankingStart,
            CrtControllerRegister.HorizontalBlankingEnd => HorizontalBlankingEndRegister.Value,
            CrtControllerRegister.HorizontalRetraceStart => HorizontalSyncStart,
            CrtControllerRegister.HorizontalRetraceEnd => HorizontalSyncEndRegister.Value,
            CrtControllerRegister.VerticalTotal => VerticalTotal,
            CrtControllerRegister.Overflow => OverflowRegister.Value,
            CrtControllerRegister.PresetRowScan => PresetRowScanRegister.Value,
            CrtControllerRegister.CharacterCellHeight => MaximumScanlineRegister.Value,
            CrtControllerRegister.CursorStart => TextCursorStartRegister.Value,
            CrtControllerRegister.CursorEnd => TextCursorEndRegister.Value,
            CrtControllerRegister.StartAddressHigh => ScreenStartAddressHigh,
            CrtControllerRegister.StartAddressLow => ScreenStartAddressLow,
            CrtControllerRegister.CursorLocationHigh => TextCursorLocationHigh,
            CrtControllerRegister.CursorLocationLow => TextCursorLocationLow,
            CrtControllerRegister.VerticalRetraceStart => VerticalSyncStart,
            CrtControllerRegister.VerticalRetraceEnd => VerticalSyncEndRegister.Value,
            CrtControllerRegister.VerticalDisplayEnd => VerticalDisplayEnd,
            CrtControllerRegister.Offset => Offset,
            CrtControllerRegister.UnderlineLocation => UnderlineRowScanlineRegister.Value,
            CrtControllerRegister.VerticalBlankingStart => VerticalBlankingStart,
            CrtControllerRegister.VerticalBlankingEnd => VerticalBlankingEnd,
            CrtControllerRegister.CrtModeControl => CrtModeControlRegister.Value,
            CrtControllerRegister.LineCompare => LineCompare,
            _ => 0
        };
    }

    /// <summary>
    ///     Writes to a CRT controller register.
    /// </summary>
    /// <param name="register">Address of register to write.</param>
    /// <param name="value">Value to write to register.</param>
    public void WriteRegister(CrtControllerRegister register, byte value) {
        switch (register) {
            case CrtControllerRegister.HorizontalTotal:
                HorizontalTotal = value;
                break;

            case CrtControllerRegister.HorizontalDisplayEnd:
                HorizontalDisplayEnd = value;
                break;

            case CrtControllerRegister.HorizontalBlankingStart:
                HorizontalBlankingStart = value;
                break;

            case CrtControllerRegister.HorizontalBlankingEnd:
                HorizontalBlankingEndRegister.Value = value;
                break;

            case CrtControllerRegister.HorizontalRetraceStart:
                HorizontalSyncStart = value;
                break;

            case CrtControllerRegister.HorizontalRetraceEnd:
                HorizontalSyncEndRegister.Value = value;
                break;

            case CrtControllerRegister.VerticalTotal:
                VerticalTotal = value;
                break;

            case CrtControllerRegister.Overflow:
                OverflowRegister.Value = value;
                break;

            case CrtControllerRegister.PresetRowScan:
                PresetRowScanRegister.Value = value;
                break;

            case CrtControllerRegister.CharacterCellHeight:
                MaximumScanlineRegister.Value = value;
                break;

            case CrtControllerRegister.CursorStart:
                TextCursorStartRegister.Value = value;
                break;

            case CrtControllerRegister.CursorEnd:
                TextCursorEndRegister.Value = value;
                break;

            case CrtControllerRegister.StartAddressHigh:
                ScreenStartAddressHigh = value;
                break;

            case CrtControllerRegister.StartAddressLow:
                ScreenStartAddressLow = value;
                break;

            case CrtControllerRegister.CursorLocationHigh:
                TextCursorLocationHigh = value;
                break;

            case CrtControllerRegister.CursorLocationLow:
                TextCursorLocationLow = value;
                break;

            case CrtControllerRegister.VerticalRetraceStart:
                VerticalSyncStart = value;
                break;

            case CrtControllerRegister.VerticalRetraceEnd:
                VerticalSyncEndRegister.Value = value;
                break;

            case CrtControllerRegister.VerticalDisplayEnd:
                VerticalDisplayEnd = value;
                break;

            case CrtControllerRegister.Offset:
                Offset = value;
                break;

            case CrtControllerRegister.UnderlineLocation:
                UnderlineRowScanlineRegister.Value = value;
                break;

            case CrtControllerRegister.VerticalBlankingStart:
                VerticalBlankingStart = value;
                break;

            case CrtControllerRegister.VerticalBlankingEnd:
                VerticalBlankingEnd = value;
                break;

            case CrtControllerRegister.CrtModeControl:
                CrtModeControlRegister.Value = value;
                break;

            case CrtControllerRegister.LineCompare:
                LineCompare = value;
                break;
        }
    }
}