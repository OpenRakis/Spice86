namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// One memory place the generated code accesses at a constant offset through a segment register:
/// which register (the CPU's <see cref="SegmentRegisterIndex"/>, what the printer maps back to
/// <c>DS</c>/<c>ES</c>/...), the 16-bit offset and the access width in bytes.
/// </summary>
internal readonly record struct ConstantMemoryAccess(SegmentRegisterIndex Segment, ushort Offset,
    int Width) : IComparable<ConstantMemoryAccess> {
    public int CompareTo(ConstantMemoryAccess other) {
        int segment = Segment.CompareTo(other.Segment);
        if (segment != 0) {
            return segment;
        }
        int offset = Offset.CompareTo(other.Offset);
        if (offset != 0) {
            return offset;
        }
        return Width.CompareTo(other.Width);
    }
}
