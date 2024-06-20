namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;

using Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Prefix representing that the instruction segmented address register will be overridden by segmentRegisterIndex
/// </summary>
public class SegmentOverrideInstructionPrefix : InstructionPrefix {
    public SegmentOverrideInstructionPrefix(InstructionField<byte> prefixField,
        SegmentRegisterIndex segmentRegisterIndex) : base(prefixField) {
        SegmentRegisterIndex = segmentRegisterIndex;
    }

    /// <summary>
    /// SegmentRegisterIndex defined in this prefix
    /// </summary>
    public SegmentRegisterIndex SegmentRegisterIndex { get; }

    /// <summary>
    /// value of the SegmentRegisterIndex
    /// </summary>
    public uint SegmentRegisterIndexValue { get => (uint)SegmentRegisterIndex; }
}