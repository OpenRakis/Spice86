namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Receives the (call site, target) pair of a switchable memory-indirect call so the CFG can
/// speculatively link the call instruction to the target its pointer holds.
/// </summary>
public interface ICallTargetRegistry {
    /// <summary>The registry of a run without speculative exploration: registrations are ignored.</summary>
    static ICallTargetRegistry None { get; } = new NullCallTargetRegistry();

    /// <summary>
    /// Links the call instruction at <paramref name="callSite"/> to <paramref name="target"/> on a
    /// best-effort basis: when either is not safely linkable, runtime discovery wires the edge.
    /// </summary>
    /// <param name="callSite">Address of the call instruction.</param>
    /// <param name="target">Target the call's pointer currently holds.</param>
    void RegisterCallTarget(SegmentedAddress callSite, SegmentedAddress target);

    private sealed class NullCallTargetRegistry : ICallTargetRegistry {
        public void RegisterCallTarget(SegmentedAddress callSite, SegmentedAddress target) {
        }
    }
}
