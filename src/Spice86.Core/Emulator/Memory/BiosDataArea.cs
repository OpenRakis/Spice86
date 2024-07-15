namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

/// <summary>
/// Provides access to emulated memory mapped BIOS values. <br/>
/// <see href="https://flint.cs.yale.edu/feng/cos/resources/BIOS/Resources/biosdata.htm" />
/// </summary>
public sealed class BiosDataArea : MemoryBasedDataStructure
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">The memory bus.</param>
    public BiosDataArea(IByteReaderWriter byteReaderWriter) : base(byteReaderWriter, MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataSegment, 0))
    {
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

    /// <summary>
    /// Gets or sets the amount of installed conventional memory in KB.
    /// </summary>
    public required ushort ConventionalMemorySizeKb { get => UInt16[0x13]; init => UInt16[0x13] = value; }

    // Padding at 0x15

    /// <summary>
    /// Gets or sets the PS/2 CTRL flag.
    /// </summary>
    public byte Ps2CtrlFlag { get => UInt8[0x16]; set => UInt8[0x16] = value; }

    /// <summary>
    /// Gets or sets the Keyboard status flag
    /// </summary>
    public ushort KbdFlag0 { get => UInt16[0x17]; set => UInt16[0x17] = value; }

    /// <summary>
    /// Gets or sets the Alt + Numpad data
    /// </summary>
    public byte AltKeypad { get => UInt8[0x19]; set => UInt8[0x19] = value; }

    /// <summary>
    /// Gets or sets the keyboard buffer head.
    /// </summary>
    public ushort KbdBufHead { get => UInt16[0x1A]; set => UInt16[0x1A] = value; }

    /// <summary>
    /// Gets or sets the keyboard buffer tail.
    /// </summary>
    public ushort KbdBufTail { get => UInt16[0x1C]; set => UInt16[0x1C] = value; }

    /// <summary>
    /// Gets the keyboard buffer.
    /// </summary>
    public UInt8Array KbdBuf => GetUInt8Array(0x1E, 32);

    /// <summary>
    /// Gets or sets the floppy recalibration status.
    /// </summary>
    public byte FloppyRecalibrationStatus { get => UInt8[0x3E]; set => UInt8[0x3E] = value; }

    /// <summary>
    /// Gets or sets the floppy motor status.
    /// </summary>
    public byte FloppyMotorStatus { get => UInt8[0x3F]; set => UInt8[0x3F] = value; }

    /// <summary>
    /// Gets or sets the value of the disk motor timer.
    /// </summary>
    public byte FloppyMotorCounter { get => UInt8[0x40]; set => UInt8[0x40] = value; }

    /// <summary>
    /// Gets or sets the last floppy status.
    /// </summary>
    public byte FloppyLastStatus { get => UInt8[0x41]; set => UInt8[0x41] = value; }

    /// <summary>
    /// Gets the floppy return status.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the video mode set register.
    /// </summary>
    public byte VideoMsr { get => UInt8[0x65]; set => UInt8[0x65] = value; }

    /// <summary>
    /// Gets or sets the video palette.
    /// </summary>
    public byte VideoPal { get => UInt8[0x66]; set => UInt8[0x66] = value; }

    /// <summary>
    /// CS:IP for 286 return from protected mode <br/>
    /// OR  Temp storage for SS:SP during shutdown <br/>
    /// OR  Day counter on all products after AT <br/>
    /// OR  PS/2 Pointer to reset code with memory preserved
    /// </summary>
    public SegmentedAddress Jump { get => SegmentedAddress[0x67]; set => SegmentedAddress[0x67] = value; }

    // Padding at 0x6B

    /// <summary>
    /// Gets or sets the current value of the Int1A counter.
    /// </summary>
    public uint TimerCounter { get => UInt32[0x6C]; set => UInt32[0x6C] = value; }

    /// <summary>
    /// Clock rollover flag, set when TimerCounter exceeds 24hrs
    /// </summary>
    public byte TimerRollover { get => UInt8[0x70]; set => UInt8[0x70] = value; }

    /// <summary>
    /// Gets or sets the break flag.
    /// </summary>
    public byte BreakFlag { get => UInt8[0x71]; set => UInt8[0x71] = value; }

    /// <summary>
    /// Gets or sets the soft reset flag.
    /// </summary>
    public ushort SoftResetFlag { get => UInt16[0x72]; set => UInt16[0x72] = value; }

    /// <summary>
    /// Gets or sets the last disk status.
    /// </summary>
    public byte DiskLastStatus { get => UInt8[0x74]; set => UInt8[0x74] = value; }

    /// <summary>
    /// Gets or sets the hard disk count.
    /// </summary>
    public byte Hdcount { get => UInt8[0x75]; set => UInt8[0x75] = value; }

    /// <summary>
    /// Gets or sets the disk control byte.
    /// </summary>
    public byte DiskControlByte { get => UInt8[0x76]; set => UInt8[0x76] = value; }

    /// <summary>
    /// Gets or sets the disk port.
    /// </summary>
    public byte PortDisk { get => UInt8[0x77]; set => UInt8[0x77] = value; }

    /// <summary>
    /// Gets the LPT timeout.
    /// </summary>
    public UInt8Array LptTimeout => GetUInt8Array(0x78, 4);

    /// <summary>
    /// Gets the COM timeout.
    /// </summary>
    public UInt8Array ComTimeout => GetUInt8Array(0x7C, 4);

    /// <summary>
    /// Gets or sets the keyboard buffer start offset.
    /// </summary>
    public ushort KbdBufStartOffset { get => UInt16[0x80]; set => UInt16[0x80] = value; }

    /// <summary>
    /// Gets or sets the keyboard buffer end offset.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the last floppy data rate.
    /// </summary>
    public byte FloppyLastDataRate { get => UInt8[0x8B]; set => UInt8[0x8B] = value; }

    /// <summary>
    /// Gets or sets the disk status controller.
    /// </summary>
    public byte DiskStatusController { get => UInt8[0x8C]; set => UInt8[0x8C] = value; }

    /// <summary>
    /// Gets or sets the disk error controller.
    /// </summary>
    public byte DiskErrorController { get => UInt8[0x8D]; set => UInt8[0x8D] = value; }

    /// <summary>
    /// Gets or sets the disk interrupt flag.
    /// </summary>
    public byte DiskInterruptFlag { get => UInt8[0x8E]; set => UInt8[0x8E] = value; }

    /// <summary>
    /// Gets or sets the floppy hard disk info.
    /// </summary>
    public byte FloppyHarddiskInfo { get => UInt8[0x8F]; set => UInt8[0x8F] = value; }

    /// <summary>
    /// Gets the floppy media state.
    /// </summary>
    public UInt8Array FloppyMediaState => GetUInt8Array(0x90, 4);

    /// <summary>
    /// Gets the floppy track.
    /// </summary>
    public UInt8Array FloppyTrack => GetUInt8Array(0x94, 2);

    /// <summary>
    /// Gets or sets the keyboard status flag.
    /// </summary>
    public byte KbdFlag1 { get => UInt8[0x96]; set => UInt8[0x96] = value; }

    /// <summary>
    /// Gets or sets the keyboard LED.
    /// </summary>
    public byte KbdLed { get => UInt8[0x97]; set => UInt8[0x97] = value; }

    /// <summary>
    /// Gets or sets the user wait complete flag.
    /// </summary>
    public SegmentedAddress UserWaitCompleteFlag { get => SegmentedAddress[0x98]; set => SegmentedAddress[0x98] = value; }

    /// <summary>
    /// Gets or sets the user wait timeout.
    /// </summary>
    public uint UserWaitTimeout { get => UInt32[0x9C]; set => UInt32[0x9C] = value; }

    /// <summary>
    /// Gets or sets the RTC wait flag.
    /// </summary>
    public byte RtcWaitFlag { get => UInt8[0xA0]; set => UInt8[0xA0] = value; }

    // 7 btyes of padding at 0xA1

    /// <summary>
    /// Gets or sets the video save table. <br/>
    ///  This includes information such as the current video mode,
    ///  and the state of the video card registers <br/>
    /// </summary>
    public SegmentedAddress VideoSaveTable { get => SegmentedAddress[0xA8]; set => SegmentedAddress[0xA8] = value; }

    /// <summary>
    /// Gets the inter application communication area.
    /// </summary>
    public UInt16Array InterApplicationCommunicationArea { get => GetUInt16Array(0xF0, 16); }

}
