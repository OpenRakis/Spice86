namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

/// <summary>
/// Represents a DTA in memory. More info here: https://stanislavs.org/helppc/dta.html
/// </summary>
public class DosDiskTransferArea : MemoryBasedDataStructureWithBaseAddress {
    private static readonly int ATTRIBUTE_OFFSET = 0x15;
    private static readonly int FILE_DATE_OFFSET = 0x18;
    private static readonly int FILE_NAME_OFFSET = 0x1E;
    private static readonly int FILE_NAME_SIZE = 13;
    private static readonly int FILE_SIZE_OFFSET = 0x1A;
    private static readonly int FILE_TIME_OFFSET = 0x16;

    public DosDiskTransferArea(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }

    public byte GetAttribute() {
        return this.GetUint8(ATTRIBUTE_OFFSET);
    }

    public ushort GetFileDate() {
        return this.GetUint16(FILE_DATE_OFFSET);
    }

    public string GetFileName() {
        return this.GetZeroTerminatedString(FILE_NAME_OFFSET, FILE_NAME_SIZE);
    }

    public ushort GetFileSize() {
        return this.GetUint16(FILE_SIZE_OFFSET);
    }

    public ushort GetFileTime() {
        return this.GetUint16(FILE_TIME_OFFSET);
    }

    public void SetAttribute(byte value) {
        this.SetUint8(ATTRIBUTE_OFFSET, value);
    }

    public void SetFileDate(ushort value) {
        this.SetUint16(FILE_DATE_OFFSET, value);
    }

    public void SetFileName(string value) {
        this.SetZeroTerminatedString(FILE_NAME_OFFSET, value, FILE_NAME_SIZE);
    }

    public void SetFileSize(ushort value) {
        this.SetUint16(FILE_SIZE_OFFSET, value);
    }

    public void SetFileTime(ushort value) {
        this.SetUint16(FILE_TIME_OFFSET, value);
    }
}