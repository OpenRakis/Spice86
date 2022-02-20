namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

using System.Text;

/// <summary>
/// Represents a MCB in memory. More info here: https://stanislavs.org/helppc/memory_control_block.html
/// </summary>
public class DosMemoryControlBlock : MemoryBasedDataStructureWithBaseAddress {
    private const int FilenameFieldSize = 8;
    private const int TypeFieldOffset = 0;
    private const int PspSegmentFieldOffset = TypeFieldOffset + 1;
    private const int SizeFieldOffset = PspSegmentFieldOffset + 2;
    private const int FilenameFieldOffset = SizeFieldOffset + 2 + 3;
    private const byte FreeMcbMarker = 0x0;
    private const byte McbLastEntry = 0x5A;
    private const byte McbNonLastEntry = 0x4D;

    public DosMemoryControlBlock(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }

    public string GetFileName() {
        return base.GetZeroTerminatedString(FilenameFieldOffset, FilenameFieldSize);
    }

    public ushort GetPspSegment() {
        return this.GetUint16(PspSegmentFieldOffset);
    }

    // Size is in paragraph (as are segments)
    public ushort GetSize() {
        return this.GetUint16(SizeFieldOffset);
    }

    public byte GetTypeField() {
        return this.GetUint8(TypeFieldOffset);
    }

    public ushort GetUsableSpaceSegment() {
        return (ushort)(MemoryUtils.ToSegment(this.GetBaseAddress()) + 1);
    }

    public bool IsFree() {
        return this.GetPspSegment() == FreeMcbMarker;
    }

    public bool IsLast() {
        return this.GetTypeField() == McbLastEntry;
    }

    public bool IsNonLast() {
        return this.GetTypeField() == McbNonLastEntry;
    }

    public bool IsValid() {
        return IsLast() || IsNonLast();
    }

    public DosMemoryControlBlock Next() {
        return new DosMemoryControlBlock(this.GetMemory(), this.GetBaseAddress() + MemoryUtils.ToPhysicalAddress((ushort)(this.GetSize() + 1), 0));
    }

    public void SetFileName(string fileName) {
        base.SetZeroTerminatedString(FilenameFieldOffset, fileName, FilenameFieldSize);
    }

    public void SetFree() {
        this.SetPspSegment(FreeMcbMarker);
        this.SetFileName("");
    }

    public void SetLast() {
        this.SetTypeField(McbLastEntry);
    }

    public void SetNonLast() {
        this.SetTypeField(McbNonLastEntry);
    }

    public void SetPspSegment(ushort value) {
        this.SetUint16(PspSegmentFieldOffset, value);
    }

    public void SetSize(ushort value) {
        this.SetUint16(SizeFieldOffset, value);
    }

    public void SetTypeField(byte value) {
        this.SetUint8(TypeFieldOffset, value);
    }

    public override string ToString() {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append($"typeField: {this.GetTypeField()}").Append($"pspSegment: {this.GetPspSegment()}").Append($"size: {this.GetSize()}").Append($"fileName: {this.GetFileName()}").ToString();
    }
}