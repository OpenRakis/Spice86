namespace Spice86.Aeon.Emulator.Video
{
    /// <summary>
    /// Emulates the VGA CRT Controller registers.
    /// </summary>
    public sealed class CrtControllerRegisters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CrtControllerRegisters"/> class.
        /// </summary>
        public CrtControllerRegisters()
        {
        }

        /// <summary>
        /// Gets or sets the Horizontal Total register.
        /// </summary>
        public byte HorizontalTotal { get; set; }
        /// <summary>
        /// Gets or sets the End Horizontal Display register.
        /// </summary>
        public byte HorizontalDisplayEnd { get; set; }
        /// <summary>
        /// Gets or sets the Start Horizontal Blanking register.
        /// </summary>
        public byte HorizontalBlankingStart { get; set; }
        /// <summary>
        /// Gets or sets the End Horizontal Blanking register.
        /// </summary>
        public byte HorizontalBlankingEnd { get; set; }
        /// <summary>
        /// Gets or sets the Start Horizontal Retrace register.
        /// </summary>
        public byte HorizontalRetraceStart { get; set; }
        /// <summary>
        /// Gets or sets the End Horizontal Retrace register.
        /// </summary>
        public byte HorizontalRetraceEnd { get; set; }
        /// <summary>
        /// Gets or sets the Vertical Total register.
        /// </summary>
        public byte VerticalTotal { get; set; }
        /// <summary>
        /// Gets or sets the Overflow register.
        /// </summary>
        public byte Overflow { get; set; }
        /// <summary>
        /// Gets or sets the Preset Row Scan register.
        /// </summary>
        public byte PresetRowScan { get; set; }
        /// <summary>
        /// Gets or sets the Maximum Scan Line register.
        /// </summary>
        public byte CharacterCellHeight { get; set; }
        /// <summary>
        /// Gets or sets the Cursor Start register.
        /// </summary>
        public byte CursorStart { get; set; }
        /// <summary>
        /// Gets or sets the Cursor End register.
        /// </summary>
        public byte CursorEnd { get; set; }
        /// <summary>
        /// Gets or sets the Start Address register.
        /// </summary>
        public ushort StartAddress { get; set; }
        /// <summary>
        /// Gets or sets the Cursor Location register.
        /// </summary>
        public ushort CursorLocation { get; set; }
        /// <summary>
        /// Gets or sets the Vertical Retrace Start register.
        /// </summary>
        public byte VerticalRetraceStart { get; set; }
        /// <summary>
        /// Gets or sets the Vertical Retrace End register.
        /// </summary>
        public byte VerticalRetraceEnd { get; set; }
        /// <summary>
        /// Gets or sets the Vertical Display End register.
        /// </summary>
        public byte VerticalDisplayEnd { get; set; }
        /// <summary>
        /// Gets or sets the Offset register.
        /// </summary>
        public byte Offset { get; set; }
        /// <summary>
        /// Gets or sets the Underline Location register.
        /// </summary>
        public byte UnderlineLocation { get; set; }
        /// <summary>
        /// Gets or sets the Start Vertical Blanking register.
        /// </summary>
        public byte VerticalBlankingStart { get; set; }
        /// <summary>
        /// Gets or sets the End Vertical Blanking register.
        /// </summary>
        public byte VerticalBlankingEnd { get; set; }
        /// <summary>
        /// Gets or sets the CRT Mode Control register.
        /// </summary>
        public byte CrtModeControl { get; set; }
        /// <summary>
        /// Gets or sets the Line Compare register.
        /// </summary>
        public byte LineCompare { get; set; }

        /// <summary>
        /// Returns the current value of a CRT controller register.
        /// </summary>
        /// <param name="address">Address of register to read.</param>
        /// <returns>Current value of the register.</returns>
        public byte ReadRegister(CrtControllerRegister address)
        {
            return address switch
            {
                CrtControllerRegister.HorizontalTotal => HorizontalTotal,
                CrtControllerRegister.HorizontalDisplayEnd => HorizontalDisplayEnd,
                CrtControllerRegister.HorizontalBlankingStart => HorizontalBlankingStart,
                CrtControllerRegister.HorizontalBlankingEnd => HorizontalBlankingEnd,
                CrtControllerRegister.HorizontalRetraceStart => HorizontalRetraceStart,
                CrtControllerRegister.HorizontalRetraceEnd => HorizontalRetraceEnd,
                CrtControllerRegister.VerticalTotal => VerticalTotal,
                CrtControllerRegister.Overflow => Overflow,
                CrtControllerRegister.PresetRowScan => PresetRowScan,
                CrtControllerRegister.CharacterCellHeight => CharacterCellHeight,
                CrtControllerRegister.CursorStart => CursorStart,
                CrtControllerRegister.CursorEnd => CursorEnd,
                CrtControllerRegister.StartAddressHigh => (byte)(StartAddress >> 8),
                CrtControllerRegister.StartAddressLow => (byte)StartAddress,
                CrtControllerRegister.CursorLocationHigh => (byte)(CursorLocation >> 8),
                CrtControllerRegister.CursorLocationLow => (byte)CursorLocation,
                CrtControllerRegister.VerticalRetraceStart => VerticalRetraceStart,
                CrtControllerRegister.VerticalRetraceEnd => VerticalRetraceEnd,
                CrtControllerRegister.VerticalDisplayEnd => VerticalDisplayEnd,
                CrtControllerRegister.Offset => Offset,
                CrtControllerRegister.UnderlineLocation => UnderlineLocation,
                CrtControllerRegister.VerticalBlankingStart => VerticalBlankingStart,
                CrtControllerRegister.VerticalBlankingEnd => VerticalBlankingEnd,
                CrtControllerRegister.CrtModeControl => CrtModeControl,
                CrtControllerRegister.LineCompare => LineCompare,
                _ => 0
            };
        }
        /// <summary>
        /// Writes to a CRT controller register.
        /// </summary>
        /// <param name="address">Address of register to write.</param>
        /// <param name="value">Value to write to register.</param>
        public void WriteRegister(CrtControllerRegister address, byte value)
        {
            switch (address)
            {
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
                    HorizontalBlankingEnd = value;
                    break;

                case CrtControllerRegister.HorizontalRetraceStart:
                    HorizontalRetraceStart = value;
                    break;

                case CrtControllerRegister.HorizontalRetraceEnd:
                    HorizontalRetraceEnd = value;
                    break;

                case CrtControllerRegister.VerticalTotal:
                    VerticalTotal = value;
                    break;

                case CrtControllerRegister.Overflow:
                    Overflow = value;
                    break;

                case CrtControllerRegister.PresetRowScan:
                    PresetRowScan = value;
                    break;

                case CrtControllerRegister.CharacterCellHeight:
                    CharacterCellHeight = value;
                    break;

                case CrtControllerRegister.CursorStart:
                    CursorStart = value;
                    break;

                case CrtControllerRegister.CursorEnd:
                    CursorEnd = value;
                    break;

                case CrtControllerRegister.StartAddressHigh:
                    StartAddress &= 0x000000FF;
                    StartAddress |= (ushort)(value << 8);
                    break;

                case CrtControllerRegister.StartAddressLow:
                    StartAddress &= 0x0000FF00;
                    StartAddress |= value;
                    break;

                case CrtControllerRegister.CursorLocationHigh:
                    CursorLocation &= 0x000000FF;
                    CursorLocation |= (ushort)(value << 8);
                    break;

                case CrtControllerRegister.CursorLocationLow:
                    CursorLocation &= 0x0000FF00;
                    CursorLocation |= value;
                    break;

                case CrtControllerRegister.VerticalRetraceStart:
                    VerticalRetraceStart = value;
                    break;

                case CrtControllerRegister.VerticalRetraceEnd:
                    VerticalRetraceEnd = value;
                    break;

                case CrtControllerRegister.VerticalDisplayEnd:
                    VerticalDisplayEnd = value;
                    break;

                case CrtControllerRegister.Offset:
                    Offset = value;
                    break;

                case CrtControllerRegister.UnderlineLocation:
                    UnderlineLocation = value;
                    break;

                case CrtControllerRegister.VerticalBlankingStart:
                    VerticalBlankingStart = value;
                    break;

                case CrtControllerRegister.VerticalBlankingEnd:
                    VerticalBlankingEnd = value;
                    break;

                case CrtControllerRegister.CrtModeControl:
                    CrtModeControl = value;
                    break;

                case CrtControllerRegister.LineCompare:
                    LineCompare = value;
                    break;
            }
        }
    }
}
