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

    public string FileName { get => base.GetZeroTerminatedString(FilenameFieldOffset, FilenameFieldSize); set => base.SetZeroTerminatedString(FilenameFieldOffset, value, FilenameFieldSize); }

    public ushort PspSegment { get => this.GetUint16(PspSegmentFieldOffset); set => this.SetUint16(PspSegmentFieldOffset, value); }

    /// <summary>
    /// <see cref="Size"/> is in paragraph (as are segments)
    /// </summary>
    public ushort Size { get => this.GetUint16(SizeFieldOffset); set => this.SetUint16(SizeFieldOffset, value); }

    public byte TypeField { get => this.GetUint8(TypeFieldOffset); set => this.SetUint8(TypeFieldOffset, value); }

    public ushort UsableSpaceSegment => (ushort)(MemoryUtils.ToSegment(this.BaseAddress) + 1);

    public bool IsFree => this.PspSegment == FreeMcbMarker;

    public bool IsLast => this.TypeField == McbLastEntry;

    public bool IsNonLast => this.TypeField == McbNonLastEntry;

    public bool IsValid =>  IsLast || IsNonLast;

    public DosMemoryControlBlock Next() {
        return new DosMemoryControlBlock(Memory, BaseAddress + MemoryUtils.ToPhysicalAddress((ushort)(Size + 1), 0));
    }

    public void SetFree() {
        this.PspSegment = FreeMcbMarker;
        this.FileName = "";
    }

    public void SetLast() {
        this.TypeField = McbLastEntry;
    }

    public void SetNonLast() {
        this.TypeField = McbNonLastEntry;
    }

    public override string ToString() {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append($"typeField: {TypeField}").Append($"pspSegment: {PspSegment}").Append($"size: {Size}").Append($"fileName: {FileName}").ToString();
    }
}