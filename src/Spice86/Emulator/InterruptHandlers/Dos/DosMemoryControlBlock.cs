namespace Spice86.Emulator.InterruptHandlers.Dos;

using Spice86.Emulator.Memory;
using Spice86.Emulator.ReverseEngineer;

using System.Text;

/// <summary>
/// Represents a MCB in memory. More info here: https://stanislavs.org/helppc/memory_control_block.html
/// </summary>
public class DosMemoryControlBlock : MemoryBasedDataStructureWithBaseAddress
{
    private static readonly int MCB_NON_LAST_ENTRY = 0x4D;
    private static readonly int MCB_LAST_ENTRY = 0x5A;
    private static readonly int FREE_MCB_MARKER = 0x0;
    private static readonly int TYPE_FIELD_OFFSET = 0;
    private static readonly int PSP_SEGMENT_FIELD_OFFSET = TYPE_FIELD_OFFSET + 1;
    private static readonly int SIZE_FIELD_OFFSET = PSP_SEGMENT_FIELD_OFFSET + 2;
    private static readonly int FILENAME_FIELD_OFFSET = SIZE_FIELD_OFFSET + 2 + 3;
    private static readonly int FILENAME_FIELD_SIZE = 8;
    public DosMemoryControlBlock(Memory memory, int baseAddress) : base(memory, baseAddress)
    {
    }

    public  bool IsValid()
    {
        return IsLast() || IsNonLast();
    }

    public  bool IsLast()
    {
        return this.GetTypeField() == MCB_LAST_ENTRY;
    }

    public  void SetLast()
    {
        this.SetTypeField(MCB_LAST_ENTRY);
    }

    public  bool IsNonLast()
    {
        return this.GetTypeField() == MCB_NON_LAST_ENTRY;
    }

    public  void SetNonLast()
    {
        this.SetTypeField(MCB_NON_LAST_ENTRY);
    }

    public  bool IsFree()
    {
        return this.GetPspSegment() == FREE_MCB_MARKER;
    }

    public  void SetFree()
    {
        this.SetPspSegment(FREE_MCB_MARKER);
        this.SetFileName("");
    }

    public  DosMemoryControlBlock Next()
    {
        return new DosMemoryControlBlock(this.GetMemory(), this.GetBaseAddress() + MemoryUtils.ToPhysicalAddress(this.GetSize() + 1, 0));
    }

    public  int GetUseableSpaceSegment()
    {
        return MemoryUtils.ToSegment(this.GetBaseAddress()) + 1;
    }

    public int GetTypeField()
    {
        return this.GetUint8(TYPE_FIELD_OFFSET);
    }

    public  void SetTypeField(int value)
    {
        this.SetUint8(TYPE_FIELD_OFFSET, (byte)value);
    }

    public  int GetPspSegment()
    {
        return this.GetUint16(PSP_SEGMENT_FIELD_OFFSET);
    }

    public  void SetPspSegment(int value)
    {
        this.SetUint16(PSP_SEGMENT_FIELD_OFFSET, (ushort)value);
    }

    // Size is in paragraph (as are segments)
    public  int GetSize()
    {
        return this.GetUint16(SIZE_FIELD_OFFSET);
    }

    public  void SetSize(int value)
    {
        this.SetUint16(SIZE_FIELD_OFFSET, (ushort)value);
    }

    public  string GetFileName()
    {
        return base.GetZeroTerminatedString(FILENAME_FIELD_OFFSET, FILENAME_FIELD_SIZE);
    }

    public  void SetFileName(string fileName)
    {
        base.SetZeroTerminatedString(FILENAME_FIELD_OFFSET, fileName, FILENAME_FIELD_SIZE);
    }

    public override string ToString()
    {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append($"typeField: {this.GetTypeField()}").Append($"pspSegment: {this.GetPspSegment()}").Append($"size: {this.GetSize()}").Append($"fileName: {this.GetFileName()}").ToString();
    }
}
