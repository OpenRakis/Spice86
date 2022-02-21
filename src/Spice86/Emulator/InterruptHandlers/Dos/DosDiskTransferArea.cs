namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

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

    public byte Attribute { get => this.GetUint8(AttributeOffset); set => this.SetUint8(AttributeOffset, value); }

    public ushort FileDate { get => this.GetUint16(FileDateOffset); set => this.SetUint16(FileDateOffset, value); }

    public string FileName { get => this.GetZeroTerminatedString(FileNameOffset, FileNameSize); set => this.SetZeroTerminatedString(FileNameOffset, value, FileNameSize); }

    public ushort FileSize { get => this.GetUint16(FileSizeOffset); set => this.SetUint16(FileSizeOffset, value); }

    public ushort FileTime { get => this.GetUint16(FileTimeOffset); set => this.SetUint16(FileTimeOffset, value); }
}