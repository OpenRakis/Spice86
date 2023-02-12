namespace Spice86.Core.Emulator.OperatingSystem;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;

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

    public string FileName { get => GetZeroTerminatedString(FilenameFieldOffset, FilenameFieldSize); set => SetZeroTerminatedString(FilenameFieldOffset, value, FilenameFieldSize); }

    public ushort PspSegment { get => GetUint16(PspSegmentFieldOffset); set => SetUint16(PspSegmentFieldOffset, value); }

    /// <summary>
    /// <see cref="Size"/> is in paragraph (as are segments)
    /// </summary>
    public ushort Size { get => GetUint16(SizeFieldOffset); set => SetUint16(SizeFieldOffset, value); }

    public byte TypeField { get => GetUint8(TypeFieldOffset); set => SetUint8(TypeFieldOffset, value); }

    public ushort UsableSpaceSegment => (ushort)(MemoryUtils.ToSegment(BaseAddress) + 1);

    public bool IsFree => PspSegment == FreeMcbMarker;

    public bool IsLast => TypeField == McbLastEntry;

    public bool IsNonLast => TypeField == McbNonLastEntry;

    public bool IsValid => IsLast || IsNonLast;

    public DosMemoryControlBlock Next() {
        return new DosMemoryControlBlock(Memory, BaseAddress + MemoryUtils.ToPhysicalAddress((ushort)(Size + 1), 0));
    }

    public void SetFree() {
        PspSegment = FreeMcbMarker;
        FileName = "";
    }

    public void SetLast() {
        TypeField = McbLastEntry;
    }

    public void SetNonLast() {
        TypeField = McbNonLastEntry;
    }

    public override string ToString() {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append("typeField: ").Append(TypeField).Append("pspSegment: ").Append(PspSegment).Append("size: ").Append(Size).Append("fileName: ").Append(FileName).ToString();
    }
}