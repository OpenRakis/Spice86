namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

using System.Text;

/// <summary>
/// Represents a MCB in memory. More info here: https://stanislavs.org/helppc/memory_control_block.html
/// </summary>
public class DosMemoryControlBlock : MemoryBasedDataStructureWithBaseAddress {
    private static readonly int FILENAME_FIELD_SIZE = 8;
    private static readonly int TYPE_FIELD_OFFSET = 0;
    private static readonly int PSP_SEGMENT_FIELD_OFFSET = TYPE_FIELD_OFFSET + 1;
    private static readonly int SIZE_FIELD_OFFSET = PSP_SEGMENT_FIELD_OFFSET + 2;
    private static readonly int FILENAME_FIELD_OFFSET = SIZE_FIELD_OFFSET + 2 + 3;
    private static readonly byte FREE_MCB_MARKER = 0x0;
    private static readonly byte MCB_LAST_ENTRY = 0x5A;
    private static readonly byte MCB_NON_LAST_ENTRY = 0x4D;

    public DosMemoryControlBlock(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }

    public string GetFileName() {
        return base.GetZeroTerminatedString(FILENAME_FIELD_OFFSET, FILENAME_FIELD_SIZE);
    }

    public ushort GetPspSegment() {
        return this.GetUint16(PSP_SEGMENT_FIELD_OFFSET);
    }

    // Size is in paragraph (as are segments)
    public ushort GetSize() {
        return this.GetUint16(SIZE_FIELD_OFFSET);
    }

    public byte GetTypeField() {
        return this.GetUint8(TYPE_FIELD_OFFSET);
    }

    public ushort GetUsableSpaceSegment() {
        return (ushort)(MemoryUtils.ToSegment(this.GetBaseAddress()) + 1);
    }

    public bool IsFree() {
        return this.GetPspSegment() == FREE_MCB_MARKER;
    }

    public bool IsLast() {
        return this.GetTypeField() == MCB_LAST_ENTRY;
    }

    public bool IsNonLast() {
        return this.GetTypeField() == MCB_NON_LAST_ENTRY;
    }

    public bool IsValid() {
        return IsLast() || IsNonLast();
    }

    public DosMemoryControlBlock Next() {
        return new DosMemoryControlBlock(this.GetMemory(), this.GetBaseAddress() + MemoryUtils.ToPhysicalAddress((ushort)(this.GetSize() + 1), 0));
    }

    public void SetFileName(string fileName) {
        base.SetZeroTerminatedString(FILENAME_FIELD_OFFSET, fileName, FILENAME_FIELD_SIZE);
    }

    public void SetFree() {
        this.SetPspSegment(FREE_MCB_MARKER);
        this.SetFileName("");
    }

    public void SetLast() {
        this.SetTypeField(MCB_LAST_ENTRY);
    }

    public void SetNonLast() {
        this.SetTypeField(MCB_NON_LAST_ENTRY);
    }

    public void SetPspSegment(ushort value) {
        this.SetUint16(PSP_SEGMENT_FIELD_OFFSET, value);
    }

    public void SetSize(ushort value) {
        this.SetUint16(SIZE_FIELD_OFFSET, value);
    }

    public void SetTypeField(byte value) {
        this.SetUint8(TYPE_FIELD_OFFSET, value);
    }

    public override string ToString() {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append($"typeField: {this.GetTypeField()}").Append($"pspSegment: {this.GetPspSegment()}").Append($"size: {this.GetSize()}").Append($"fileName: {this.GetFileName()}").ToString();
    }
}