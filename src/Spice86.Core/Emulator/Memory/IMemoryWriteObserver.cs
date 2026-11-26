namespace Spice86.Core.Emulator.Memory;

/// <summary>
/// Receives one event per write to the memory bus, with the width the write was issued with. Set it on
/// <see cref="MemoryWriteEvents.Observer"/> of <see cref="IMemory.WriteEvents"/>.
/// </summary>
/// <remarks>
/// <para>The memory bus stores a 16-bit or 32-bit write as 2 or 4 byte writes, and each byte write triggers
/// the memory write breakpoints. This event comes once per write, after the MMU has accepted the access and
/// before the first byte is stored, so before the breakpoints of its bytes. A write the MMU refuses with a
/// fault reports nothing.</para>
/// <para>Writes through the indexers of <see cref="IMemory"/> (<c>UInt8</c>, <c>UInt16</c>, <c>UInt32</c>,
/// the signed and big-endian ones, linear or segmented) and through
/// <see cref="IMemory.WriteUInt16Segmented"/> and <see cref="IMemory.WriteUInt32Segmented"/> raise it.
/// The CPU and generated code write through them, so both report:</para>
/// <list type="bullet">
/// <item>one write per memory operand an instruction stores, and one per repetition of a string
/// instruction (<c>REP STOSW</c> with CX = 5 reports five 16-bit writes);</item>
/// <item>one write per push, and for <c>PUSHA</c> one per register;</item>
/// <item>for a 16-bit far call or an interrupt, the segment, then the offset of the return address, as two
/// 16-bit writes; for a 32-bit far call, the 32-bit offset, the segment and a zero padding word.</item>
/// </list>
/// <para>Generated code produced without <c>GeneratorOptions.KeepStoreWidths</c> stores two adjacent word
/// stores as one 32-bit write, made where the second store was. A far pointer written through
/// <c>SegmentedAddress16</c> or <c>SegmentedAddress32</c> reports its offset, then its segment. Writes
/// through <see cref="IMemory"/>'s byte indexer <c>this[uint]</c>, <see cref="IMemory.SneakilyWrite"/>,
/// <see cref="IMemory.WriteRam"/> and memory based data structures raise no event: only the breakpoints see
/// them.</para>
/// <para>The observer runs on the emulation thread. It must not write memory.</para>
/// </remarks>
public interface IMemoryWriteObserver {
    /// <summary>
    /// Called when a write is about to be stored.
    /// </summary>
    /// <param name="write">The address, width and value of the write.</param>
    void OnMemoryWrite(in MemoryWrite write);
}
