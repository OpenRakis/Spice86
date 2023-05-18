namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using System.Text;

using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Utils;

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

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="baseAddress">the address of the structure in memory.</param>
    public DosMemoryControlBlock(Memory memory, uint baseAddress) : base(memory, baseAddress) {
    }
    /// <summary>
    /// Gets or sets the name of the file associated with the MCB.
    /// </summary>
    public string FileName { get => GetZeroTerminatedString(FilenameFieldOffset, FilenameFieldSize); set => SetZeroTerminatedString(FilenameFieldOffset, value, FilenameFieldSize); }

    /// <summary>
    /// Gets or sets the PSP segment associated with the MCB.
    /// </summary>
    public ushort PspSegment { get => GetUint16(PspSegmentFieldOffset); set => SetUint16(PspSegmentFieldOffset, value); }

    /// <summary>
    /// Gets or sets the size of the MCB in paragraphs.
    /// </summary>
    /// <remarks>
    /// The size of an MCB is always specified in paragraphs.
    /// </remarks>
    public ushort Size { get => GetUint16(SizeFieldOffset); set => SetUint16(SizeFieldOffset, value); }

    /// <summary>
    /// Gets or sets the type field of the MCB.
    /// </summary>
    /// <remarks>
    /// The type field of an MCB specifies whether it is the last or non-last entry in the chain.
    /// </remarks>
    public byte TypeField { get => GetUint8(TypeFieldOffset); set => SetUint8(TypeFieldOffset, value); }

    /// <summary>
    /// Gets the usable space segment associated with the MCB.
    /// </summary>
    public ushort UsableSpaceSegment => (ushort)(MemoryUtils.ToSegment(BaseAddress) + 1);

    /// <summary>
    /// Gets a value indicating whether the MCB is free.
    /// </summary>
    public bool IsFree => PspSegment == FreeMcbMarker;

    /// <summary>
    /// Gets a value indicating whether the MCB is the last entry in the chain.
    /// </summary>
    public bool IsLast => TypeField == McbLastEntry;

    /// <summary>
    /// Gets a value indicating whether the MCB is a non-last entry in the chain.
    /// </summary>
    public bool IsNonLast => TypeField == McbNonLastEntry;
    
    
    /// <summary>
    /// Returns if the MCB is valid.
    /// </summary>
    public bool IsValid => IsLast || IsNonLast;

    /// <summary>
    /// Returns the next MCB in the MCB chain.
    /// </summary>
    /// <returns></returns>
    public DosMemoryControlBlock Next() {
        return new DosMemoryControlBlock(Memory, BaseAddress + MemoryUtils.ToPhysicalAddress((ushort)(Size + 1), 0));
    }

    /// <summary>
    /// Releases the MCB.
    /// </summary>
    public void SetFree() {
        PspSegment = FreeMcbMarker;
        FileName = "";
    }

    /// <summary>
    /// Sets the MCB <see cref="TypeField"/> as 0x5A.
    /// </summary>
    public void SetLast() {
        TypeField = McbLastEntry;
    }

    /// <summary>
    /// Sets the MCB <see cref="TypeField"/> as 0x4D.
    /// </summary>
    public void SetNonLast() {
        TypeField = McbNonLastEntry;
    }

    /// <inheritdoc />
    public override string ToString() {
        return new StringBuilder(System.Text.Json.JsonSerializer.Serialize(this)).Append("typeField: ").Append(TypeField).Append("pspSegment: ").Append(PspSegment).Append("size: ").Append(Size).Append("fileName: ").Append(FileName).ToString();
    }
}