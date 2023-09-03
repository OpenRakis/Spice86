namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.ComponentModel.DataAnnotations;

/// <summary>
///     Represents a Dynamic Functionality State Table in memory, a data structure used in IBM PC VGA emulation to keep
///     track of the current state of video functionality. <br />
/// </summary>
public class VideoFunctionalityInfo : MemoryBasedDataStructure {
    /// <summary>
    ///     Initializes a new instance of the VideoFunctionalityInfo class with the specified memory and baseAddress.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The base address of the memory structure</param>
    public VideoFunctionalityInfo(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    ///     Gets or sets the address of the Saved Functionality Table (SFT) in memory.
    /// </summary>
    public uint SftAddress { get => UInt32[0x00]; set => UInt32[0x00] = value; }

    /// <summary>
    ///     Gets or sets the current video mode.
    /// </summary>
    public byte VideoMode { get => UInt8[0x04]; set => UInt8[0x04] = value; }

    /// <summary>
    ///     Gets or sets the number of columns in the current video mode.
    /// </summary>
    public ushort ScreenColumns { get => UInt16[0x05]; set => UInt16[0x05] = value; }

    /// <summary>
    ///     Gets or sets the length of the video buffer in bytes.
    /// </summary>
    public ushort VideoBufferLength { get => UInt16[0x07]; set => UInt16[0x07] = value; }

    /// <summary>
    ///     Gets or sets the base address of the video buffer in memory.
    /// </summary>
    public ushort VideoBufferAddress { get => UInt16[0x09]; set => UInt16[0x09] = value; }

    /// <summary>
    ///     Gets or sets the last scan line of the cursor.
    /// </summary>
    public byte CursorEndLine { get => UInt8[0x1B]; set => UInt8[0x1B] = value; }

    /// <summary>
    ///     Gets or sets the first scan line of the cursor.
    /// </summary>
    public byte CursorStartLine { get => UInt8[0x1C]; set => UInt8[0x1C] = value; }

    /// <summary>
    ///     Gets or sets the currently active display page.
    /// </summary>
    public byte ActiveDisplayPage { get => UInt8[0x1D]; set => UInt8[0x1D] = value; }

    /// <summary>
    ///     Gets or sets the base address of the CRT controller.
    /// </summary>
    public ushort CrtControllerBaseAddress { get => UInt16[0x1E]; set => UInt16[0x1E] = value; }

    /// <summary>
    ///     Gets or sets the current value of register 3x8.
    /// </summary>
    public byte CurrentRegister3X8Value { get => UInt8[0x20]; set => UInt8[0x20] = value; }

    /// <summary>
    ///     Gets or sets the current value of register 3x9.
    /// </summary>
    public byte CurrentRegister3X9Value { get => UInt8[0x21]; set => UInt8[0x21] = value; }

    /// <summary>
    ///     Gets or sets the number of rows on the screen.
    /// </summary>
    public byte ScreenRows { get => UInt8[0x22]; set => UInt8[0x22] = value; }

    /// <summary>
    ///     Gets or sets the height of the character matrix.
    /// </summary>
    public ushort CharacterMatrixHeight { get => UInt16[0x23]; set => UInt16[0x23] = value; }

    /// <summary>
    ///     Gets or sets the code for the active display combination.
    /// </summary>
    public byte ActiveDisplayCombinationCode { get => UInt8[0x25]; set => UInt8[0x25] = value; }

    /// <summary>
    ///     Gets or sets the code for the alternate display combination.
    /// </summary>
    public byte AlternateDisplayCombinationCode { get => UInt8[0x26]; set => UInt8[0x26] = value; }

    /// <summary>
    ///     Gets or sets the number of colors supported.
    /// </summary>
    public ushort NumberOfColorsSupported { get => UInt16[0x27]; set => UInt16[0x27] = value; }

    /// <summary>
    ///     Gets or sets the number of pages.
    /// </summary>
    public byte NumberOfPages { get => UInt8[0x29]; set => UInt8[0x29] = value; }

    /// <summary>
    ///     Gets or sets the number of active scan lines.
    /// </summary>
    public byte NumberOfActiveScanLines { get => UInt8[0x2A]; set => UInt8[0x2A] = value; }

    /// <summary>
    ///     Gets or sets the text character table used.
    /// </summary>
    public byte TextCharacterTableUsed { get => UInt8[0x2B]; set => UInt8[0x2B] = value; }

    /// <summary>
    ///     Gets or sets the second text character table used.
    /// </summary>
    public byte TextCharacterTableUsed2 { get => UInt8[0x2C]; set => UInt8[0x2C] = value; }

    /// <summary>
    ///     Gets or sets the other state information.
    /// </summary>
    public byte OtherStateInformation { get => UInt8[0x2D]; set => UInt8[0x2D] = value; }

    /// <summary>
    ///     Gets or sets the available video RAM.
    /// </summary>
    public byte VideoRamAvailable { get => UInt8[0x31]; set => UInt8[0x31] = value; }

    /// <summary>
    ///     Gets or sets the value of the video functionality information's save area status register.
    /// </summary>
    public byte SaveAreaStatus { get => UInt8[0x32]; set => UInt8[0x32] = value; }

    /// <summary>
    ///     Gets the current cursor position for the specified display page.
    /// </summary>
    /// <param name="page">The display page to retrieve the cursor position for (0-7).</param>
    /// <returns>A tuple containing the X and Y coordinates of the cursor position.</returns>
    public (byte, byte) GetCursorPosition([Range(0, 7)] int page) {
        return (UInt8[0x0B + page * 2], UInt8[0x0C + page * 2]);
    }

    /// <summary>
    ///     Sets the current cursor position for the specified display page.
    /// </summary>
    /// <param name="page">The display page to set the cursor position for (0-7).</param>
    /// <param name="x">The X coordinate of the cursor position.</param>
    /// <param name="y">The Y coordinate of the cursor position.</param>
    public void SetCursorPosition([Range(0, 7)] int page, byte x, byte y) {
        int offset = page * 2;
        UInt8[0x0B + offset] = x;
        UInt8[0x0C + offset] = y;
    }
}