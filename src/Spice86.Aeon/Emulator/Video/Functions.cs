namespace Spice86.Aeon.Emulator.Video; 

/// <summary>
/// Contains constants representing various functions that can be performed using the IBM VGA video controller.
/// </summary>
public static class Functions {
    /// <summary>
    /// Sets the video mode.
    /// </summary>
    public const byte SetVideoMode = 0x00;
    
    /// <summary>
    /// Sets the cursor type.
    /// </summary>
    public const byte SetCursorType = 0x01;
    
    /// <summary>
    /// Sets the cursor position.
    /// </summary>
    public const byte SetCursorPosition = 0x02;
    
    /// <summary>
    /// Gets the current cursor position.
    /// </summary>
    public const byte GetCursorPosition = 0x03;
    
    /// <summary>
    /// Reads the light pen data.
    /// </summary>
    public const byte ReadLightPen = 0x04;
    
    /// <summary>
    /// Selects the active display page.
    /// </summary>
    public const byte SelectActiveDisplayPage = 0x05;
    
    /// <summary>
    /// Scrolls the active page up.
    /// </summary>
    public const byte ScrollActivePageUp = 0x06;
    
    /// <summary>
    /// Scrolls the active page down.
    /// </summary>
    public const byte ScrollActivePageDown = 0x07;
    
    /// <summary>
    /// Reads the character and attribute at the current cursor position.
    /// </summary>
    public const byte ReadCharacterAndAttributeAtCursor = 0x08;
    
    /// <summary>
    /// Writes the character and attribute at the current cursor position.
    /// </summary>
    public const byte WriteCharacterAndAttributeAtCursor = 0x09;
    
    /// <summary>
    /// Writes the character at the current cursor position.
    /// </summary>
    public const byte WriteCharacterAtCursor = 0x0A;
    
    /// <summary>
    /// Performs video operations.
    /// </summary>
    public const byte Video = 0x0B;
    
    /// <summary>
    /// Sets the background color of the video.
    /// </summary>
    public const byte Video_SetBackgroundColor = 0x00;
    
    /// <summary>
    /// Sets the palette of the video.
    /// </summary>
    public const byte Video_SetPalette = 0x01;
    
    /// <summary>
    /// Writes the graphics pixel at the specified coordinate.
    /// </summary>
    public const byte WriteGraphicsPixelAtCoordinate = 0x0C;
    
    /// <summary>
    /// Reads the graphics pixel at the specified coordinate.
    /// </summary>
    public const byte ReadGraphicsPixelAtCoordinate = 0x0D;
    
    /// <summary>
    /// Writes the text in teletype mode.
    /// </summary>
    public const byte WriteTextInTeletypeMode = 0x0E;
    
    /// <summary>
    /// Gets the current video mode.
    /// </summary>
    public const byte GetVideoMode = 0x0F;
    
    /// <summary>
    /// Performs palette operations.
    /// </summary>
    public const byte Palette = 0x10;
    
    /// <summary>
    /// Sets a single palette register.
    /// </summary>
    public const byte Palette_SetSingleRegister = 0x00;
    
    /// <summary>
    /// Sets the border color of the palette.
    /// </summary>
    public const byte Palette_SetBorderColor = 0x01;
    
    /// <summary>
    /// Sets all palette registers.
    /// </summary>
    public const byte Palette_SetAllRegisters = 0x02;
    /// <summary>
    /// Toggle blinking attribute of DAC registers.
    /// </summary>
    public const byte Palette_ToggleBlink = 0x03;

    /// <summary>
    /// Set a single DAC register.
    /// </summary>
    public const byte Palette_SetSingleDacRegister = 0x10;

    /// <summary>
    /// Set multiple DAC registers.
    /// </summary>
    public const byte Palette_SetDacRegisters = 0x12;

    /// <summary>
    /// Select a DAC color page.
    /// </summary>
    public const byte Palette_SelectDacColorPage = 0x13;

    /// <summary>
    /// Read a single DAC register.
    /// </summary>
    public const byte Palette_ReadSingleDacRegister = 0x15;

    /// <summary>
    /// Read multiple DAC registers.
    /// </summary>
    public const byte Palette_ReadDacRegisters = 0x17;

    /// <summary>
    /// Specifies a font used in text mode.
    /// </summary>
    public const byte Font = 0x11;

    /// <summary>
    /// Load an 8x8 font.
    /// </summary>
    public const byte Font_Load8x8 = 0x12;

    /// <summary>
    /// Load an 8x16 font.
    /// </summary>
    public const byte Font_Load8x16 = 0x14;

    /// <summary>
    /// Retrieve font information.
    /// </summary>
    public const byte Font_GetFontInfo = 0x30;

    /// <summary>
    /// Extended Graphics Array (EGA) graphics mode.
    /// </summary>
    public const byte EGA = 0x12;

    /// <summary>
    /// Get EGA information.
    /// </summary>
    public const byte EGA_GetInfo = 0x10;

    /// <summary>
    /// Select vertical resolution for EGA mode.
    /// </summary>
    public const byte EGA_SelectVerticalResolution = 0x30;

    /// <summary>
    /// Load a palette in EGA mode.
    /// </summary>
    public const byte EGA_PaletteLoading = 0x31;

    /// <summary>
    /// Get display combination code.
    /// </summary>
    public const byte GetDisplayCombinationCode = 0x1A;

    /// <summary>
    /// Get functionality information.
    /// </summary>
    public const byte GetFunctionalityInfo = 0x1B;
}