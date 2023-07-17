namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Utils;

/// <summary>
/// Provides access to emulated memory mapped BIOS values.
/// </summary>
public sealed class BiosDataArea : MemoryBasedDataStructure {
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    public BiosDataArea(IByteReaderWriter byteReaderWriter) : base(byteReaderWriter, MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataSegment, 0)) {
    }

    /// <summary>
    /// Gets or sets the flags that indicate which hardware is installed.
    /// </summary>
    public ushort EquipmentListFlags { get => UInt16[0x0010]; set => UInt16[0x0010] = value; }

    /// <summary>
    /// Gets or sets the value of the disk motor timer.
    /// </summary>
    public byte DiskMotorTimer { get => UInt8[0x0040]; set => UInt8[0x0040] = value; }

    /// <summary>
    /// Gets or sets the BIOS video mode.
    /// </summary>
    public byte VideoMode { get => UInt8[0x0049]; set => UInt8[0x0049] = value; }

    /// <summary>
    /// Gets or sets the BIOS screen column count.
    /// </summary>
    public ushort ScreenColumns { get => UInt16[0x004A]; set => UInt16[0x004A] = value; }

    /// <summary>
    /// Gets or sets the size of active video page in bytes.
    /// </summary>
    public ushort VideoPageSize { get => UInt16[0x004C]; set => UInt16[0x004C] = value; }

    /// <summary>
    /// Gets or sets the offset address of the active video page relative to the start of video RAM
    /// </summary>
    public ushort VideoPageStart { get => UInt16[0x004E]; set => UInt16[0x004E] = value; }

    /// <summary>
    /// Cursor positions for the 8 text pages.
    /// </summary>
    public UInt16Array CursorPosition => GetUInt16Array(0x0050, 8);

    /// <summary>
    /// Gets or sets the BIOS cursor type.
    /// </summary>
    public ushort CursorType { get => UInt16[0x0060]; set => UInt16[0x0060] = value; }

    /// <summary>
    /// Gets or sets the currently active video page.
    /// </summary>
    public byte CurrentVideoPage { get => UInt8[0x0062]; set => UInt8[0x0062] = value; }

    /// <summary>
    /// Gets or sets the CRT controller I/O port address.
    /// </summary>
    public ushort CrtControllerBaseAddress { get => UInt16[0x0063]; set => UInt16[0x0063] = value; }

    /// <summary>
    /// Gets or sets the current value of the real time clock.
    /// </summary>
    public uint RealTimeClock { get => UInt32[0x006C]; set => UInt32[0x006C] = value; }

    /// <summary>
    /// Gets or sets the screen row count.
    /// </summary>
    public byte ScreenRows { get => UInt8[0x0084]; set => UInt8[0x0084] = value; }

    /// <summary>
    /// Gets or sets the character point height.
    /// </summary>
    public ushort CharacterHeight { get => UInt16[0x0085]; set => UInt16[0x0085] = value; }

    /// <summary>
    /// Gets or sets the VideoCtl.
    /// </summary>
    public byte VideoCtl { get => UInt8[0x0087]; set => UInt8[0x0087] = value; }

    /// <summary>
    /// Gets or sets the BIOS video mode options.
    /// </summary>
    public byte FeatureSwitches { get => UInt8[0x0088]; set => UInt8[0x0088] = value; }

    /// <summary>
    /// Gets or sets the EGA feature switch values.
    /// </summary>
    public byte ModesetCtl { get => UInt8[0x0089]; set => UInt8[0x0089] = value; }

    /// <summary>
    /// Gets or sets the display combination code.
    /// </summary>
    public byte DisplayCombinationCode { get => UInt8[0x008A]; set => UInt8[0x008A] = value; }
}