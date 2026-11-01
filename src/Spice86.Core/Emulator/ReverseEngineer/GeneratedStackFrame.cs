namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;

/// <summary>
/// The slot accessors of the per-method frame structs the code generator emits next to a
/// generated method: a named view over the slots of the method's stack frame, addressed by
/// entry SP + entry offset. Reads and writes go through the segmented <see cref="IMemory"/>
/// indexers with the live SS value, exactly what the raw <c>UInt8/16/32[SS, ...]</c> access
/// would have done. A struct so a generated method allocates nothing for its frame.
/// </summary>
public readonly struct GeneratedStackFrame {
    private readonly IMemory _memory;
    private readonly SegmentRegisters _segmentRegisters;
    private readonly ushort _entrySp;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="segmentRegisters">The CPU segment registers (SS is read at access time).</param>
    /// <param name="entrySp">The SP value the method was entered with.</param>
    public GeneratedStackFrame(IMemory memory, SegmentRegisters segmentRegisters, ushort entrySp) {
        _memory = memory;
        _segmentRegisters = segmentRegisters;
        _entrySp = entrySp;
    }

    private ushort Ss => _segmentRegisters.UInt16[(uint)SegmentRegisterIndex.SsIndex];

    private ushort Offset(int entryOffset) => (ushort)(_entrySp + entryOffset);

    /// <summary>Byte at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public byte GetByte(int entryOffset) => _memory.UInt8[Ss, Offset(entryOffset), SegmentAccessKind.Stack];

    /// <summary>Writes a byte at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public void SetByte(int entryOffset, byte value) => _memory.UInt8[Ss, Offset(entryOffset), SegmentAccessKind.Stack] = value;

    /// <summary>Word at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public ushort GetWord(int entryOffset) => _memory.UInt16[Ss, Offset(entryOffset), SegmentAccessKind.Stack];

    /// <summary>Writes a word at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public void SetWord(int entryOffset, ushort value) => _memory.UInt16[Ss, Offset(entryOffset), SegmentAccessKind.Stack] = value;

    /// <summary>Double word at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public uint GetDword(int entryOffset) => _memory.UInt32[Ss, Offset(entryOffset), SegmentAccessKind.Stack];

    /// <summary>Writes a double word at <c>SS:[entry SP + entryOffset]</c>.</summary>
    public void SetDword(int entryOffset, uint value) => _memory.UInt32[Ss, Offset(entryOffset), SegmentAccessKind.Stack] = value;
}
