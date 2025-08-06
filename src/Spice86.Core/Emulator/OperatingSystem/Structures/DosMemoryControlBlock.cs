﻿namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Utils;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Represents a MCB in memory.
/// </summary>
[DebuggerDisplay("Owner = {Owner}, AllocationSizeInBytes = {AllocationSizeInBytes}, IsFree = {IsFree}, IsValid = {IsValid}, IsLast = {IsLast}")]
public class DosMemoryControlBlock : MemoryBasedDataStructure {
    private const int FilenameFieldSize = 8;
    private const int TypeFieldOffset = 0;
    private const int PspSegmentFieldOffset = TypeFieldOffset + 1;
    private const int SizeFieldOffset = PspSegmentFieldOffset + 2;
    private const int FilenameFieldOffset = SizeFieldOffset + 2 + 3;
    public const byte FreeMcbMarker = 0x0;
    private const byte McbLastEntry = 0x5A;
    private const byte McbNonLastEntry = 0x4D;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">the address of the structure in memory.</param>
    public DosMemoryControlBlock(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the type field of the MCB.
    /// </summary>
    /// <remarks>
    /// The type field of an MCB specifies whether it is the last or non-last entry in the chain.
    /// </remarks>
    public byte TypeField { get => UInt8[TypeFieldOffset]; set => UInt8[TypeFieldOffset] = value; }

    /// <summary>
    /// Gets or sets the name of the file associated with the MCB.
    /// </summary>
    [Range(0, 8)]
    public string Owner { get => GetZeroTerminatedString(FilenameFieldOffset, FilenameFieldSize); set => SetZeroTerminatedString(FilenameFieldOffset, value, FilenameFieldSize); }

    /// <summary>
    /// Gets or sets the PSP segment associated with the MCB.
    /// </summary>
    public ushort PspSegment { get => UInt16[PspSegmentFieldOffset]; set => UInt16[PspSegmentFieldOffset] = value; }

    /// <summary>
    /// Gets or sets the size of the MCB in paragraphs (16 bytes).
    /// </summary>
    /// <remarks>
    /// The size of an MCB is always specified in paragraphs.
    /// </remarks>
    public ushort Size { get => UInt16[SizeFieldOffset]; set => UInt16[SizeFieldOffset] = value; }

    public int AllocationSizeInBytes => Size * 16;

    /// <summary>
    /// Gets the usable space segment associated with the MCB.
    /// </summary>
    /// <remarks>
    /// Allocation starts here and extends for <see cref="Size"/> * 16 bytes.
    /// </remarks>
    public ushort DataBlockSegment => (ushort)(MemoryUtils.ToSegment(BaseAddress) + 1);

    /// <summary>
    /// Gets a value indicating whether the MCB is free.
    /// </summary>
    /// <remarks>
    /// It is free only if the PspSegment is <c>0</c>
    /// </remarks>
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
    /// Returns if the MCB is valid (must be Last or NonLast).
    /// </summary>
    public bool IsValid => IsLast || IsNonLast;

    /// <summary>
    /// Returns the next MCB in the MCB in chain, or null if not found.
    /// </summary>
    /// <returns>The next MCB if found, <see langword="null" /> otherwise.</returns>
    public DosMemoryControlBlock? GetNextOrDefault() {
        uint baseAddress = BaseAddress + MemoryUtils.ToPhysicalAddress((ushort)(Size + 1), 0);
        if (baseAddress >= ByteReaderWriter.Length) {
            return null;
        }
        DosMemoryControlBlock next = new(ByteReaderWriter, baseAddress);
        return next;
    }

    /// <summary>
    /// Gets the Psp as <see cref="DosProgramSegmentPrefix"/> structure,
    /// or <see langword="null"/> if <see cref="PspSegment"/> is not set.
    /// </summary>
    public DosProgramSegmentPrefix? ProgramSegmentPrefix => PspSegment == 0 ? null :
        new DosProgramSegmentPrefix(ByteReaderWriter, MemoryUtils.ToPhysicalAddress(PspSegment, 0));

    /// <summary>
    /// Releases the MCB.
    /// </summary>
    public void SetFree() {
        PspSegment = FreeMcbMarker;
        Owner = "";
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
        return new StringBuilder("IsValid").Append(IsValid)
            .Append(" IsFree:").Append(IsFree)
            .Append(" IsLast:").Append(IsLast)
            .Append(" IsNonLast:").Append(IsNonLast)
            .Append(" BaseAddress: ").Append(BaseAddress)
            .Append(" UsableSpaceSegment: ").Append(DataBlockSegment)
            .Append(" TypeField: ").Append(TypeField)
            .Append(" PspSegment: ").Append(PspSegment)
            .Append(" Size: ").Append(Size)
            .Append(" FileName: ").Append(Owner)
            .ToString();
    }
}