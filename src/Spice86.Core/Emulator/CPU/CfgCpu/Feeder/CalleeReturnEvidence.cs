namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Cache of CFG facts about whether a callee entry is known to return to the call site's
/// continuation. Per callee entry two flags are kept: an aligned return
/// (<see cref="InstructionSuccessorType.CallToReturn"/> link produced by an observed ret) and an
/// unaligned one (<see cref="InstructionSuccessorType.CallToMisalignedReturn"/> or
/// <see cref="InstructionSuccessorType.CallToRedirectedReturn"/>). A callee is known to return when
/// it has aligned evidence and no unaligned evidence; once unaligned evidence exists the callee is
/// never trusted again, while continuations already explored keep their edges.
/// </summary>
internal sealed class CalleeReturnEvidence {
    private readonly HashSet<SegmentedAddress> _aligned = [];
    private readonly HashSet<SegmentedAddress> _unaligned = [];

    /// <summary>
    /// Records one observed call-to-return-target link for <paramref name="calleeEntry"/>.
    /// </summary>
    /// <returns><c>true</c> only the first time the callee becomes known to return.</returns>
    public bool Record(SegmentedAddress calleeEntry, bool aligned) {
        if (!aligned) {
            _unaligned.Add(calleeEntry);
            return false;
        }
        return _aligned.Add(calleeEntry) && !_unaligned.Contains(calleeEntry);
    }

    /// <summary>True when the callee has aligned return evidence and no unaligned one.</summary>
    public bool IsKnownToReturn(SegmentedAddress calleeEntry) =>
        _aligned.Contains(calleeEntry) && !_unaligned.Contains(calleeEntry);
}
