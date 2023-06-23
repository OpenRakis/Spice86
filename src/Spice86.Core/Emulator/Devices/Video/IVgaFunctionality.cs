namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The VGA services for text and graphics
/// </summary>
public interface IVgaFunctionality {
    /// <summary>
    ///     Write a text string to the screen at the current cursor position and page.
    /// </summary>
    /// <param name="text"></param>
    void WriteString(string text);

    /// <summary>
    ///     Writes a string to the video buffer.
    /// </summary>
    /// <param name="segment">Segment of address to load string from</param>
    /// <param name="offset">Offset of address to load string from</param>
    /// <param name="length">String length</param>
    /// <param name="includeAttributes">True when memory contains character-attribute pairs</param>
    /// <param name="defaultAttribute">Attribute to use when the string does not contain attributes</param>
    /// <param name="cursorPosition">Position where to start writing</param>
    /// <param name="updateCursorPosition">True to set the current cursor at the end of the string</param>
    void WriteString(ushort segment, ushort offset, ushort length, bool includeAttributes, byte defaultAttribute, CursorPosition cursorPosition, bool updateCursorPosition);

    /// <summary>
    ///     Sat an internal EGA palette index to a value.
    /// </summary>
    /// <param name="register">Which of the 16 palette entries to write</param>
    /// <param name="value">The value to write</param>
    void SetEgaPaletteRegister(byte register, byte value);

    /// <summary>
    ///     Set the overscan / border color.
    /// </summary>
    /// <param name="colorIndex">Which palette entry to use for overscan / border</param>
    void SetOverscanBorderColor(byte colorIndex);

    /// <summary>
    ///     Set the palette registers from values in memory
    /// </summary>
    /// <param name="segment">Segment of that address where to load the values from</param>
    /// <param name="offset">Segment of that address where to load the values from</param>
    void SetAllPaletteRegisters(ushort segment, ushort offset);

    /// <summary>
    ///     Fill the internal palette
    /// </summary>
    /// <param name="values">The 16 values to fill the palette with</param>
    void SetAllPaletteRegisters(byte[] values);

    /// <summary>
    ///     Toggle between intensity and blinking as function fo the msb of a characters attribute.
    /// </summary>
    /// <param name="enabled">True for intensity, false for blinking</param>
    void ToggleIntensity(bool enabled);

    /// <summary>
    ///     Read an entry from the internal palette;
    /// </summary>
    /// <param name="index">Palette index</param>
    byte ReadPaletteRegister(byte index);

    /// <summary>
    ///     Read the overscan / border color.
    /// </summary>
    byte GetOverscanBorderColor();

    /// <summary>
    ///     Copy the internal palette to the specified memory location.
    /// </summary>
    /// <param name="segment">Segment of address to copy to</param>
    /// <param name="offset">Offset of address to copy to</param>
    void GetAllPaletteRegisters(ushort segment, ushort offset);

    /// <summary>
    ///     Get the internal palette.
    /// </summary>
    byte[] GetAllPaletteRegisters();

    /// <summary>
    ///     Specify a color in the palette.
    /// </summary>
    /// <param name="index">Index of the color to set</param>
    /// <param name="red">6-bit red component</param>
    /// <param name="green">6-bit green component</param>
    /// <param name="blue">6-bit blue component</param>
    void WriteToDac(byte index, byte red, byte green, byte blue);

    /// <summary>
    ///     Copy colors from a memory location into the palette.
    /// </summary>
    /// <param name="segment">Segment of the address to load from.</param>
    /// <param name="offset">Offset of the address to load from.</param>
    /// <param name="startIndex">Destination start index</param>
    /// <param name="count">Amount of colors to copy</param>
    void WriteToDac(ushort segment, ushort offset, byte startIndex, ushort count);

    /// <summary>
    ///     Set the P5/P4 select bit.
    /// </summary>
    void SetP5P4Select(bool enabled);

    /// <summary>
    ///     Set the color select register.
    /// </summary>
    /// <param name="value"></param>
    void SetColorSelectRegister(byte value);

    /// <summary>
    ///     Get palette entries.
    /// </summary>
    /// <param name="startIndex">Start index in the palette.</param>
    /// <param name="count">Amount of entries to return</param>
    /// <returns></returns>
    byte[] ReadFromDac(byte startIndex, int count);

    /// <summary>
    ///     Copy palette entries to memory.
    /// </summary>
    /// <param name="segment">Segment of address to copy to.</param>
    /// <param name="offset">Offset of address to copy to.</param>
    /// <param name="startIndex">Palette index where to start copying</param>
    /// <param name="count">Amount of entries to copy</param>
    void ReadFromDac(ushort segment, ushort offset, byte startIndex, ushort count);

    /// <summary>
    ///     Set the Pixel Mask register.
    /// </summary>
    /// <param name="value"></param>
    void WriteToPixelMask(byte value);

    /// <summary>
    ///     Get the Pixel Mask Register value.
    /// </summary>
    byte ReadPixelMask();

    /// <summary>
    ///     Get the state of the color page functionality.
    /// </summary>
    ushort ReadColorPageState();

    /// <summary>
    ///     Convert palette entries to grayscale
    /// </summary>
    /// <param name="start">Palette index to start graying</param>
    /// <param name="count">Amount of entries to process</param>
    void PerformGrayScaleSumming(byte start, int count);

    /// <summary>
    ///     Get the current VGA mode.
    /// </summary>
    VgaMode GetCurrentMode();

    /// <summary>
    ///     Get the cursor position on the specified page.
    /// </summary>
    /// <param name="currentVideoPage">Which of the 8 pages to get the cursor from</param>
    /// <returns></returns>
    CursorPosition GetCursorPosition(byte currentVideoPage);

    /// <summary>
    ///     Set the cursor position on the current page.
    /// </summary>
    /// <param name="cursorPosition"></param>
    void SetCursorPosition(CursorPosition cursorPosition);

    /// <summary>
    ///     Write the specified character to the specified cursor position on the current page.
    /// </summary>
    /// <param name="cursorPosition"></param>
    /// <param name="characterPlusAttribute"></param>
    /// <returns></returns>
    CursorPosition WriteTeletype(CursorPosition cursorPosition, CharacterPlusAttribute characterPlusAttribute);

    /// <summary>
    ///     Write the specified character to the current cursor position on the current page.
    /// </summary>
    /// <param name="characterPlusAttribute"></param>
    void WriteTextInTeletypeMode(CharacterPlusAttribute characterPlusAttribute);

    /// <summary>
    ///     Set the border color to the specified entry.
    /// </summary>
    /// <param name="color">palette index</param>
    void SetBorderColor(byte color);

    /// <summary>
    ///     Switch the curren palette to the specified id.
    /// </summary>
    void SetPalette(byte id);

    /// <summary>
    ///     Write a character to the specified page a number of times.
    /// </summary>
    /// <param name="characterPlusAttribute">The character to write</param>
    /// <param name="page">The page to write to</param>
    /// <param name="count">How many times to repeat the character</param>
    void WriteCharacterAtCursor(CharacterPlusAttribute characterPlusAttribute, byte page, int count);

    /// <summary>
    ///     Get the character at the specified cursor position on the current page.
    /// </summary>
    /// <param name="cursorPosition"></param>
    CharacterPlusAttribute ReadChar(CursorPosition cursorPosition);

    /// <summary>
    ///     Switch to the specified page.
    /// </summary>
    /// <param name="page"></param>
    /// <returns>Start address of the specified page.</returns>
    int SetActivePage(byte page);

    /// <summary>
    ///     Set the shape of the text cursor.
    /// </summary>
    /// <param name="cursorType"></param>
    void SetCursorShape(ushort cursorType);

    /// <summary>
    ///     Read the color at the specified location.
    /// </summary>
    /// <param name="x">x-coordinate</param>
    /// <param name="y">y-coordinate</param>
    /// <returns>Color index of the pixel</returns>
    byte ReadPixel(ushort x, ushort y);

    /// <summary>
    ///     Set the specified pixel to the color index.
    /// </summary>
    /// <param name="color">palette index</param>
    /// <param name="x">x-coordinate</param>
    /// <param name="y">y-coordinate</param>
    void WritePixel(byte color, ushort x, ushort y);

    /// <summary>
    ///     Switch the current mode to the specified one.
    /// </summary>
    /// <param name="modeId">Numeric id of the mode</param>
    /// <param name="flags">specify switching behaviour</param>
    void VgaSetMode(int modeId, ModeFlags flags);

    /// <summary>
    ///     Get the value oif the feature switches register.
    /// </summary>
    /// <returns></returns>
    byte GetFeatureSwitches();

    /// <summary>
    ///     Get whether the current mode is a color mode.
    /// </summary>
    /// <returns></returns>
    bool GetColorMode();

    /// <summary>
    ///     Switch between 200, 350 and 400 scanline modes.
    /// </summary>
    void SelectScanLines(int lines);

    /// <summary>
    ///     Specify whether to use switch to the default palette when loading a new mode.
    /// </summary>
    /// <param name="enabled"></param>
    void DefaultPaletteLoading(bool enabled);

    /// <summary>
    ///     Enable or disable video addressing.
    /// </summary>
    /// <param name="enabled"></param>
    void EnableVideoAddressing(bool enabled);

    /// <summary>
    ///     Enable or disable the gray scale summing.
    /// </summary>
    /// <param name="enabled"></param>
    void SummingToGrayScales(bool enabled);

    /// <summary>
    ///     Enable or disable the cursor emulation.
    /// </summary>
    /// <param name="enabled"></param>
    void CursorEmulation(bool enabled);

    /// <summary>
    ///     Load a user specified font into font memory.
    /// </summary>
    /// <param name="segment">Segment of the address to read from</param>
    /// <param name="offset">Offset of the address to read from</param>
    /// <param name="length">Amount of characters to copy</param>
    /// <param name="start">Character index to start at</param>
    /// <param name="fontBlock">Which font-block to write to</param>
    /// <param name="height">How many pixels high the font is</param>
    void LoadUserFont(ushort segment, ushort offset, ushort length, ushort start, byte fontBlock, byte height);

    /// <summary>
    ///     Load a font into font memory.
    /// </summary>
    /// <param name="fontBytes">The bytes of the font</param>
    /// <param name="length">Amount of characters to copy</param>
    /// <param name="start">Character index to start at</param>
    /// <param name="fontBlock">Which font-block to write to</param>
    /// <param name="height">How many pixels high the font is</param>
    void LoadFont(byte[] fontBytes, ushort length, ushort start, byte fontBlock, byte height);

    /// <summary>
    ///     Set the location of font map A and B in plane 2 memory.
    /// </summary>
    void SetFontBlockSpecifier(byte fontBlock);

    /// <summary>
    ///     Set the amount of scan lines to use per row.
    /// </summary>
    void SetScanLines(byte lines);

    /// <summary>
    ///     Point the 8x8 font to the specified location.
    /// </summary>
    void LoadUserCharacters8X8(ushort segment, ushort offset);

    /// <summary>
    ///     Point the 8x14 font to the specified location and specify height and screen rows.
    /// </summary>
    /// <param name="rowSpecifier">0 = user-specified, 1 = 14, 3 = 43, default = 25</param>
    /// <param name="userSpecifiedRows">Amount of rows when rowSpecifier is 0</param>
    void LoadUserGraphicsCharacters(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecifiedRows);

    /// <summary>
    ///     Load the 8x14 font and set the amount of screen rows.
    /// </summary>
    /// <param name="rowSpecifier">0 = user-specified, 1 = 14, 3 = 43, default = 25</param>
    /// <param name="userSpecifiedRows">Amount of rows when rowSpecifier is 0</param>
    void LoadRom8X14Font(byte rowSpecifier, byte userSpecifiedRows);

    /// <summary>
    ///     Load the 8x8 font and set the amount of screen rows.
    /// </summary>
    /// <param name="rowSpecifier">0 = user-specified, 1 = 14, 3 = 43, default = 25</param>
    /// <param name="userSpecifiedRows">Amount of rows when rowSpecifier is 0</param>
    void LoadRom8X8Font(byte rowSpecifier, byte userSpecifiedRows);

    /// <summary>
    ///     Load the 8x16 font and set the amount of screen rows.
    /// </summary>
    /// <param name="rowSpecifier">0 = user-specified, 1 = 14, 3 = 43, default = 25</param>
    /// <param name="userSpecifiedRows">Amount of rows when rowSpecifier is 0</param>
    void LoadGraphicsRom8X16Font(byte rowSpecifier, byte userSpecifiedRows);

    /// <summary>
    ///     Get the address of the specified font.
    /// </summary>
    /// <param name="font">0-8</param>
    SegmentedAddress GetFontAddress(byte font);

    /// <summary>
    ///     Scroll a text area
    /// </summary>
    void VerifyScroll(int direction, byte upperLeftX, byte upperLeftY, byte lowerRightX, byte lowerRightY, int lines, byte attribute);

    /// <summary>
    ///     Clear an area of the screen.
    /// </summary>
    /// <param name="startPosition"></param>
    /// <param name="area"></param>
    /// <param name="characterPlusAttribute"></param>
    void ClearCharacters(CursorPosition startPosition, Area area, CharacterPlusAttribute characterPlusAttribute);

    /// <summary>
    ///     Move an area of text on the screen.
    /// </summary>
    void MoveChars(CursorPosition dest, Area area, int lines);

    /// <summary>
    ///     Load custom font and set the amount of screen rows.
    /// </summary>
    void LoadGraphicsFont(ushort segment, ushort offset, byte height, byte rowSpecifier, byte userSpecifiedRows);
    
    /// <summary>
    ///    Notifies when the video mode has changed.
    /// </summary>
    event EventHandler<VideoModeChangedEventArgs> VideoModeChanged;
}