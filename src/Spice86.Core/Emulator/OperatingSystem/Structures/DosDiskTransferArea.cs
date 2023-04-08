namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// Represents a DTA in memory. More info here: https://stanislavs.org/helppc/dta.html
/// </summary>
public class DosDiskTransferArea : MemoryBasedDataStructureWithBaseAddress {
    private const int AttributeOffset = 0x15;
    private const int FileDateOffset = 0x18;
    private const int FileNameOffset = 0x1E;
    private const int FileNameSize = 13;
    private const int FileSizeOffset = 0x1A;
    private const int FileTimeOffset = 0x16;

    public DosDiskTransferArea(Memory memory, uint baseAddress) : base(memory, baseAddress) { }

    public byte Attribute { get => GetUint8(AttributeOffset); set => SetUint8(AttributeOffset, value); }

    public ushort FileDate { get => GetUint16(FileDateOffset); set => SetUint16(FileDateOffset, value); }

    public string FileName {
        get => GetZeroTerminatedString(FileNameOffset, FileNameSize);
        set => SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }

    public ushort FileSize { get => GetUint16(FileSizeOffset); set => SetUint16(FileSizeOffset, value); }

    public ushort FileTime { get => GetUint16(FileTimeOffset); set => SetUint16(FileTimeOffset, value); }
}