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

    public DosDiskTransferArea(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }

    public byte GetAttribute() {
        return this.GetUint8(AttributeOffset);
    }

    public ushort GetFileDate() {
        return this.GetUint16(FileDateOffset);
    }

    public string GetFileName() {
        return this.GetZeroTerminatedString(FileNameOffset, FileNameSize);
    }

    public ushort GetFileSize() {
        return this.GetUint16(FileSizeOffset);
    }

    public ushort GetFileTime() {
        return this.GetUint16(FileTimeOffset);
    }

    public void SetAttribute(byte value) {
        this.SetUint8(AttributeOffset, value);
    }

    public void SetFileDate(ushort value) {
        this.SetUint16(FileDateOffset, value);
    }

    public void SetFileName(string value) {
        this.SetZeroTerminatedString(FileNameOffset, value, FileNameSize);
    }

    public void SetFileSize(ushort value) {
        this.SetUint16(FileSizeOffset, value);
    }

    public void SetFileTime(ushort value) {
        this.SetUint16(FileTimeOffset, value);
    }
}