namespace Spice86.Aeon.Emulator.Video;

/// <summary>
/// A static class containing constant values for various ports.
/// </summary>
public static class Ports {
    /// <summary>
    /// The address of the CRT controller.
    /// </summary>
    public const int CrtControllerAddress = 0x03B4;

    /// <summary>
    /// The data port of the CRT controller.
    /// </summary>
    public const int CrtControllerData = 0x03B5;

    /// <summary>
    /// The port used for reading input status 1.
    /// </summary>
    public const int InputStatus1Read = 0x03BA;

    /// <summary>
    /// The port used for writing to the feature control register.
    /// </summary>
    public const int FeatureControlWrite = 0x03BA;

    /// <summary>
    /// The address port for the attribute controller.
    /// </summary>
    public const int AttributeAddress = 0x03C0;

    /// <summary>
    /// The data port for the attribute controller.
    /// </summary>
    public const int AttributeData = 0x03C1;

    /// <summary>
    /// The port used for reading input status 0.
    /// </summary>
    public const int InputStatus0Read = 0x03C2;

    /// <summary>
    /// The port used for writing to the miscellaneous output register.
    /// </summary>
    public const int MiscOutputWrite = 0x03C2;

    /// <summary>
    /// The address port for the sequencer.
    /// </summary>
    public const int SequencerAddress = 0x03C4;

    /// <summary>
    /// The data port for the sequencer.
    /// </summary>
    public const int SequencerData = 0x03C5;

    /// <summary>
    /// The port used for reading the state of the DAC.
    /// </summary>
    public const int DacStateRead = 0x03C7;

    /// <summary>
    /// The port used for reading the address of the DAC in read mode.
    /// </summary>
    public const int DacAddressReadMode = 0x03C7;

    /// <summary>
    /// The port used for writing to the address of the DAC in write mode.
    /// </summary>
    public const int DacAddressWriteMode = 0x03C8;

    /// <summary>
    /// The data port for the DAC.
    /// </summary>
    public const int DacData = 0x03C9;

    /// <summary>
    /// The port used for reading the feature control register.
    /// </summary>
    public const int FeatureControlRead = 0x03CA;

    /// <summary>
    /// The port used for reading the miscellaneous output register.
    /// </summary>
    public const int MiscOutputRead = 0x03CC;

    /// <summary>
    /// The address port for the graphics controller.
    /// </summary>
    public const int GraphicsControllerAddress = 0x03CE;

    /// <summary>
    /// The data port for the graphics controller.
    /// </summary>
    public const int GraphicsControllerData = 0x03CF;

    /// <summary>
    /// An alternate mirror of the address port for the CRT controller.
    /// </summary>
    public const int CrtControllerAddressAltMirror1 = 0x03D0;
    /// <summary>
    /// The alternate mirror 2 CRT controller address.
    /// </summary>
    public const int CrtControllerAddressAltMirror2 = 0x03D2;

    /// <summary>
    /// The alternate CRT controller address.
    /// </summary>
    public const int CrtControllerAddressAlt = 0x03D4;

    /// <summary>
    /// The alternate CRT controller data.
    /// </summary>
    public const int CrtControllerDataAlt = 0x03D5;

    /// <summary>
    /// The alternate input status 1 read.
    /// </summary>
    public const int InputStatus1ReadAlt = 0x03DA;

    /// <summary>
    /// The alternate feature control write.
    /// </summary>
    public const int FeatureControlWriteAlt = 0x03DA;
}

/// <summary>
/// Represents the graphics registers used by the VGA graphics controller. <br/>
/// http://www.osdever.net/FreeVGA/vga/graphreg.htm
/// </summary>
public enum GraphicsRegister
{
    /// <summary>
    /// Set/reset register. Controls which bit planes are written to in write mode 0.
    /// </summary>
    SetReset,

    /// <summary>
    /// Enable set/reset register. Enables the set/reset function for bit planes 0-3 in write mode 0.
    /// </summary>
    EnableSetReset,

    /// <summary>
    /// Color compare register. Contains mask bits for each bit plane in write mode 0 and the compare value in write mode 1.
    /// </summary>
    ColorCompare,

    /// <summary>
    /// Data rotate register. Rotates data written to the bit planes in write modes 0 and 1.
    /// </summary>
    DataRotate,

    /// <summary>
    /// Read map select register. Controls which bit planes are read from in read mode 1.
    /// </summary>
    ReadMapSelect,

    /// <summary>
    /// Graphics mode register. Controls various VGA graphics modes.
    /// </summary>
    GraphicsMode,

    /// <summary>
    /// Miscellaneous graphics register. Contains various graphics-related bits.
    /// </summary>
    MiscellaneousGraphics,

    /// <summary>
    /// Color don't care register. Contains mask bits for each bit plane in write mode 0.
    /// </summary>
    ColorDontCare,

    /// <summary>
    /// Bit mask register. Masks off bits in each bit plane in write modes 0 and 2.
    /// </summary>
    BitMask
}

/// <summary>
/// Represents the different registers available in the VGA sequencer.
/// </summary>
public enum SequencerRegister
{
    /// <summary>
    /// Reset Register: Writing a non-zero value to this register resets the sequencer to its power-on state.
    /// </summary>
    Reset,

    /// <summary>
    /// Clocking Mode Register: Controls the operation of the dot clock generator and selects the pixel clock rate.
    /// </summary>
    ClockingMode,

    /// <summary>
    /// Map Mask Register: Selects which planes are used in reading and writing to display memory.
    /// </summary>
    MapMask,

    /// <summary>
    /// Character Map Select Register: Determines the font set used to display text characters.
    /// </summary>
    CharacterMapSelect,

    /// <summary>
    /// Sequencer Memory Mode Register: Controls how the sequencer updates video memory.
    /// </summary>
    SequencerMemoryMode
}

/// <summary>
/// Defines the indices of the registers in the Attribute Controller (AC) register array.
/// </summary>
public enum AttributeControllerRegister
{
    /// <summary>
    /// Palette entry from 0 to 15.
    /// </summary>
    FirstPaletteEntry,

    /// <summary>
    /// Palette entry from 0 to 15.
    /// </summary>
    LastPaletteEntry = 0x0F,

    /// <summary>
    /// Selects a mode of operation for the Attribute Controller.
    /// </summary>
    AttributeModeControl,

    /// <summary>
    /// Selects the color to display in the overscan area.
    /// </summary>
    OverscanColor,

    /// <summary>
    /// Specifies which of the four planes of video memory to enable for reading/writing.
    /// </summary>
    ColorPlaneEnable,

    /// <summary>
    /// Controls the horizontal scrolling of the screen image.
    /// </summary>
    HorizontalPixelPanning,

    /// <summary>
    /// Selects one of the 16 possible color palettes for the screen image.
    /// </summary>
    ColorSelect
}

/// <summary>
/// CRT Controller registers enumeration.
/// </summary>
public enum CrtControllerRegister
{
    /// <summary>
    /// Register 0 - Total number of horizontal pixels per line.
    /// </summary>
    HorizontalTotal,
    
    /// <summary>
    /// Register 1 - Number of pixels to display per line.
    /// </summary>
    EndHorizontalDisplay,
    
    /// <summary>
    /// Register 2 - Number of pixels to display before horizontal retrace.
    /// </summary>
    StartHorizontalBlanking,
    
    /// <summary>
    /// Register 3 - Number of pixels to display after horizontal retrace.
    /// </summary>
    EndHorizontalBlanking,
    
    /// <summary>
    /// Register 4 - Number of pixels in horizontal retrace.
    /// </summary>
    StartHorizontalRetrace,
    
    /// <summary>
    /// Register 5 - End of horizontal retrace period.
    /// </summary>
    EndHorizontalRetrace,
    
    /// <summary>
    /// Register 6 - Total number of lines per screen.
    /// </summary>
    VerticalTotal,
    
    /// <summary>
    /// Register 7 - Bit 7 determines if a line has more than MaximumScanLine number of scanlines.
    /// </summary>
    Overflow,
    
    /// <summary>
    /// Register 8 - Number of lines in top border.
    /// </summary>
    PresetRowScan,
    
    /// <summary>
    /// Register 9 - Maximum number of scanlines per character.
    /// </summary>
    MaximumScanLine,
    
    /// <summary>
    /// Register 10 - Start scanline of cursor.
    /// </summary>
    CursorStart,
    
    /// <summary>
    /// Register 11 - End scanline of cursor.
    /// </summary>
    CursorEnd,
    
    /// <summary>
    /// Register 12 - Starting address of display memory (high byte).
    /// </summary>
    StartAddressHigh,
    
    /// <summary>
    /// Register 13 - Starting address of display memory (low byte).
    /// </summary>
    StartAddressLow,
    
    /// <summary>
    /// Register 14 - Cursor location (high byte).
    /// </summary>
    CursorLocationHigh,
    
    /// <summary>
    /// Register 15 - Cursor location (low byte).
    /// </summary>
    CursorLocationLow,
    
    /// <summary>
    /// Register 16 - Start of vertical retrace period.
    /// </summary>
    VerticalRetraceStart,
    
    /// <summary>
    /// Register 17 - End of vertical retrace period.
    /// </summary>
    VerticalRetraceEnd,
    
    /// <summary>
    /// Register 18 - End of display period.
    /// </summary>
    VerticalDisplayEnd,
    
    /// <summary>
    /// Register 19 - Display memory offset.
    /// </summary>
    Offset,
    
    /// <summary>
    /// Register 20 - Location of underline (scanline number).
    /// </summary>
    UnderlineLocation,
    
    /// <summary>
    /// Register 21 - Number of lines to display before vertical retrace.
    /// </summary>
    StartVerticalBlanking,
    
    /// <summary>
    /// Register 22 - Number of lines to display after vertical retrace.
    /// </summary>
    EndVerticalBlanking,
    
    /// <summary>
    /// Register 23 - Mode control.
    /// </summary>
    CrtModeControl,
    
    /// <summary>
    /// Register 24 - Line compare.
    /// </summary>
    LineCompare
}