namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

public class DosFileControlBlock : MemoryBasedDataStructure {
    public DosFileControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the drive letter. 0=default drive (when unopened, or A), 1=drive A (when unopened, or B), 2=drive B (when unopened, or C), etc.
    /// </summary>
    public byte Drive { get => UInt8[0x0]; set => UInt8[0x0] = value; }

    /// <summary>
    /// Space padded name of the file
    /// </summary>
    public string FileName { get => GetZeroTerminatedString(0x1, 8); set => SetZeroTerminatedString(0x1, value, 8); }

    /// <summary>
    /// Space padded extension of the file
    /// </summary>
    public string Extension { get => GetZeroTerminatedString(0x9, 3); set => SetZeroTerminatedString(0x9, value, 3); }

    public ushort CurrentBlock { get => UInt16[0x0c]; set => UInt16[0x0c] = value; }

    public ushort LogicalRecordSize { get => UInt16[0x0e]; set => UInt16[0x0e] = value; }

    public uint FileSize { get => UInt32[0x10]; set => UInt32[0x10] = value; }

    public ushort Date { get => UInt16[0x14]; set => UInt16[0x14] = value; }

    public ushort Time { get => UInt16[0x16]; set => UInt16[0x16] = value; }

    public byte SftEntries { get => UInt8[0x18]; set => UInt8[0x18] = value; }

    public byte ShareAttributes { get => UInt8[0x19]; set => UInt8[0x19] = value; }
    public byte ExtraInfo { get => UInt8[0x1A]; set => UInt8[0x1A] = value; }

    public byte FileHandle { get => UInt8[0x1B]; set => UInt8[0x1B] = value; }

    /// <summary>
    /// Reserved, undocumented
    /// </summary>
    public UInt8Array Reserved => GetUInt8Array(0x1C, 8);

    public ushort CurrentRecord { get => UInt16[0x20]; set => UInt16[0x20] = value; }

    public uint CurrentRecordNumber { get => UInt32[0x22]; set => UInt32[0x22] = value; }
}
