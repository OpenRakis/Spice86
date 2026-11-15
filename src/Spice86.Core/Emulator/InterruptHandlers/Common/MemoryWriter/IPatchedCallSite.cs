namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// A component that owns an <see cref="InMemoryAddressSwitcher"/> able to rewrite the target
/// bytes of a call instruction in emulated memory at run time.
/// </summary>
public interface IPatchedCallSite {
    /// <summary>Segmented address of the call instruction the switcher can patch, or
    /// <see langword="null"/> when it was never emitted.</summary>
    SegmentedAddress? PatchedCallAddress { get; }
}
