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

    public DosDiskTransferArea(Memory memory, int baseAddress) : base(memory, baseAddress) {
    }

    public int GetAttribute() {
        return this.GetUint8(ATTRIBUTE_OFFSET);
    }

    public int GetFileDate() {
        return this.GetUint16(FILE_DATE_OFFSET);
    }

    public string GetFileName() {
        return this.GetZeroTerminatedString(FILE_NAME_OFFSET, FILE_NAME_SIZE);
    }

    public int GetFileSize() {
        return this.GetUint16(FILE_SIZE_OFFSET);
    }

    public int GetFileTime() {
        return this.GetUint16(FILE_TIME_OFFSET);
    }

    public void SetAttribute(int value) {
        this.SetUint8(ATTRIBUTE_OFFSET, (byte)value);
    }

    public void SetFileDate(int value) {
        this.SetUint16(FILE_DATE_OFFSET, (ushort)value);
    }

    public void SetFileName(string value) {
        this.SetZeroTerminatedString(FILE_NAME_OFFSET, value, FILE_NAME_SIZE);
    }

    public void SetFileSize(int value) {
        this.SetUint16(FILE_SIZE_OFFSET, (ushort)value);
    }

    public void SetFileTime(int value) {
        this.SetUint16(FILE_TIME_OFFSET, (ushort)value);
    }
}