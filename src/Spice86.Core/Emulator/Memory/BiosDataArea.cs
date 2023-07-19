namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;
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
    /// COM1,2,3,4 port addresses
    /// </summary>
    public UInt16Array PortCom => GetUInt16Array(0x0, 4);

    /// <summary>
    /// LPT1,2,3 port addresses
    /// </summary>
    public UInt16Array PortLpt => GetUInt16Array(0x8, 3);

    /// <summary>
    /// Extended BIOS Data Area segment or LPT4 port
    /// </summary>
    public ushort EbdaSeg { get => UInt16[0xE]; set => UInt16[0xE] = value; }

    /// <summary>
    /// Gets or sets the flags that indicate which hardware is installed.
    /// </summary>
    public ushort EquipmentListFlags { get => UInt16[0x10]; set => UInt16[0x10] = value; }

    // Padding at 0x12

    public ushort MemSizeKb { get => UInt16[0x13]; set => UInt16[0x13] = value; }

    // Padding at 0x15

    public byte Ps2CtrlFlag { get => UInt8[0x16]; set => UInt8[0x16] = value; }

    public ushort KbdFlag0 { get => UInt16[0x17]; set => UInt16[0x17] = value; }

    public byte AltKeypad { get => UInt8[0x19]; set => UInt8[0x19] = value; }

    public ushort KbdBufHead { get => UInt16[0x1A]; set => UInt16[0x1A] = value; }

    public ushort KbdBufTail { get => UInt16[0x1C]; set => UInt16[0x1C] = value; }

    public UInt8Array KbdBuf => GetUInt8Array(0x1E, 32);

    public byte FloppyRecalibrationStatus { get => UInt8[0x3E]; set => UInt8[0x3E] = value; }

    public byte FloppyMotorStatus { get => UInt8[0x3F]; set => UInt8[0x3F] = value; }

    /// <summary>
    /// Gets or sets the value of the disk motor timer.
    /// </summary>
    public byte FloppyMotorCounter { get => UInt8[0x40]; set => UInt8[0x40] = value; }

    public byte FloppyLastStatus { get => UInt8[0x41]; set => UInt8[0x41] = value; }

    public UInt8Array FloppyReturnStatus => GetUInt8Array(0x42, 7);

    /// <summary>
    /// Gets or sets the BIOS video mode.
    /// </summary>
    public byte VideoMode { get => UInt8[0x49]; set => UInt8[0x49] = value; }

    /// <summary>
    /// Gets or sets the BIOS screen column count.
    /// </summary>
    public ushort ScreenColumns { get => UInt16[0x4A]; set => UInt16[0x4A] = value; }

    /// <summary>
    /// Gets or sets the size of active video page in bytes.
    /// </summary>
    public ushort VideoPageSize { get => UInt16[0x4C]; set => UInt16[0x4C] = value; }

    /// <summary>
    /// Gets or sets the offset address of the active video page relative to the start of video RAM
    /// </summary>
    public ushort VideoPageStart { get => UInt16[0x4E]; set => UInt16[0x4E] = value; }

    /// <summary>
    /// Cursor positions for the 8 text pages.
    /// </summary>
    public UInt16Array CursorPosition => GetUInt16Array(0x50, 8);

    /// <summary>
    /// Gets or sets the BIOS cursor type.
    /// </summary>
    public ushort CursorType { get => UInt16[0x60]; set => UInt16[0x60] = value; }

    /// <summary>
    /// Gets or sets the currently active video page.
    /// </summary>
    public byte CurrentVideoPage { get => UInt8[0x62]; set => UInt8[0x62] = value; }

    /// <summary>
    /// Gets or sets the CRT controller I/O port address.
    /// </summary>
    public ushort CrtControllerBaseAddress { get => UInt16[0x63]; set => UInt16[0x63] = value; }

    public byte VideoMsr { get => UInt8[0x65]; set => UInt8[0x65] = value; }

    public byte VideoPal { get => UInt8[0x66]; set => UInt8[0x66] = value; }

    /// <summary>
    ///     CS:IP for 286 return from protected mode
    /// OR  Temp storage for SS:SP during shutdown
    /// OR  Day counter on all products after AT
    /// OR  PS/2 Pointer to reset code with memory preserved
    /// </summary>
    public SegmentedAddress Jump { get => SegmentedAddress[0x67]; set => SegmentedAddress[0x67] = value; }

    /// Padding at 0x6B

    /// <summary>
    /// Gets or sets the current value of the Int1A counter.
    /// </summary>
    public uint TimerCounter { get => UInt32[0x6C]; set => UInt32[0x6C] = value; }

    /// <summary>
    /// Clock rollover flag, set when TimerCounter exceeds 24hrs
    /// </summary>
    public byte TimerRollover { get => UInt8[0x70]; set => UInt8[0x70] = value; }

    public byte BreakFlag { get => UInt8[0x71]; set => UInt8[0x71] = value; }

    public ushort SoftResetFlag { get => UInt16[0x72]; set => UInt16[0x72] = value; }

    public byte DiskLastStatus { get => UInt8[0x74]; set => UInt8[0x74] = value; }

    public byte Hdcount { get => UInt8[0x75]; set => UInt8[0x75] = value; }

    public byte DiskControlByte { get => UInt8[0x76]; set => UInt8[0x76] = value; }

    public byte PortDisk { get => UInt8[0x77]; set => UInt8[0x77] = value; }

    public UInt8Array LptTimeout => GetUInt8Array(0x78, 4);

    public UInt8Array ComTimeout => GetUInt8Array(0x7C, 4);

    public ushort KbdBufStartOffset { get => UInt16[0x80]; set => UInt16[0x80] = value; }

    public ushort KbdBufEndOffset { get => UInt16[0x82]; set => UInt16[0x82] = value; }

    /// <summary>
    /// Gets or sets the screen row count.
    /// </summary>
    public byte ScreenRows { get => UInt8[0x84]; set => UInt8[0x84] = value; }

    /// <summary>
    /// Gets or sets the character point height.
    /// </summary>
    public ushort CharacterHeight { get => UInt16[0x85]; set => UInt16[0x85] = value; }

    /// <summary>
    /// Gets or sets the VideoCtl.
    /// </summary>
    public byte VideoCtl { get => UInt8[0x87]; set => UInt8[0x87] = value; }

    /// <summary>
    /// Gets or sets the BIOS video mode options.
    /// </summary>
    public byte VideoFeatureSwitches { get => UInt8[0x88]; set => UInt8[0x88] = value; }

    /// <summary>
    /// Gets or sets the EGA feature switch values.
    /// </summary>
    public byte ModesetCtl { get => UInt8[0x89]; set => UInt8[0x89] = value; }

    /// <summary>
    /// Gets or sets the display combination code.
    /// </summary>
    public byte DisplayCombinationCode { get => UInt8[0x8A]; set => UInt8[0x8A] = value; }

    public byte FloppyLastDataRate { get => UInt8[0x8B]; set => UInt8[0x8B] = value; }

    public byte DiskStatusController { get => UInt8[0x8C]; set => UInt8[0x8C] = value; }

    public byte DiskErrorController { get => UInt8[0x8D]; set => UInt8[0x8D] = value; }

    public byte DiskInterruptFlag { get => UInt8[0x8E]; set => UInt8[0x8E] = value; }

    public byte FloppyHarddiskInfo { get => UInt8[0x8F]; set => UInt8[0x8F] = value; }

    public UInt8Array FloppyMediaState => GetUInt8Array(0x90, 4);

    public UInt8Array FloppyTrack => GetUInt8Array(0x94, 2);

    public byte KbdFlag1 { get => UInt8[0x96]; set => UInt8[0x96] = value; }

    public byte KbdLed { get => UInt8[0x97]; set => UInt8[0x97] = value; }

    public SegmentedAddress UserWaitCompleteFlag { get => SegmentedAddress[0x98]; set => SegmentedAddress[0x98] = value; }

    public uint UserWaitTimeout { get => UInt32[0x9C]; set => UInt32[0x9C] = value; }

    public byte RtcWaitFlag { get => UInt8[0xA0]; set => UInt8[0xA0] = value; }

    /// 7 btyes of padding at 0xA1
    
    public SegmentedAddress VideoSavetable { get => SegmentedAddress[0xA8]; set => SegmentedAddress[0xA8] = value; }

}