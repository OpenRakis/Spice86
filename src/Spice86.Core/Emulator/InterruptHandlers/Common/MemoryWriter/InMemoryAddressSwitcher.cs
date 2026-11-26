namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles writing and switching SegmentedAddress in emulated memory
/// </summary>
public class InMemoryAddressSwitcher {
    private readonly IIndexable _memory;
    private readonly Action<SegmentedAddress, SegmentedAddress> _registerSpeculativeTarget;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAddressSwitcher"/> class.
    /// </summary>
    /// <param name="memory">The memory bus</param>
    /// <param name="registerSpeculativeTarget">Notification invoked with <c>(call site, target)</c>
    /// each time the far pointer of a switchable call is written, so the CFG can speculatively link
    /// the call instruction to its new target.</param>
    public InMemoryAddressSwitcher(IIndexable memory,
        Action<SegmentedAddress, SegmentedAddress> registerSpeculativeTarget) {
        _memory = memory;
        _registerSpeculativeTarget = registerSpeculativeTarget;
    }

    /// <summary>
    /// Physical address of the SegmentedAddress we want to store.
    /// </summary>
    public uint? PhysicalLocation { private get; set; }

    /// <summary>
    /// Segmented address of the memory-indirect call instruction reading the pointer this switcher
    /// rewrites, set by <see cref="MemoryAsmWriter.WriteCsIndirectFarCallToSwitcherDefaultAddress"/>
    /// when it emits the call.
    /// </summary>
    public SegmentedAddress? PatchedCallAddress { get; internal set; }

    /// <summary>
    /// Where the displacement of the emitted call instruction sits in memory, fixed up by
    /// <see cref="MemoryAsmWriter.WriteSwitcherPointer"/> once the pointer's address is known.
    /// </summary>
    internal SegmentedAddress? CallDisplacementAddress { get; set; }

    /// <summary>
    /// Value of the Default Address to use.
    /// </summary>
    public SegmentedAddress? DefaultAddress { get; set; }

    /// <summary>
    /// Sets the segmented address. <see cref="PhysicalLocation"/> needs to be initialized before calling this method.
    /// </summary>
    /// <param name="segment">Segment</param>
    /// <param name="offset">Offset</param>
    public void SetAddress(ushort segment, ushort offset) {
        if (PhysicalLocation is null) {
            throw new UnrecoverableException($"Attempted to set address but location is null. Please set {nameof(PhysicalLocation)} first.");
        }
        SegmentedAddress target = new(segment, offset);
        _memory.SegmentedAddress16[PhysicalLocation.Value] = target;
        if (PatchedCallAddress is { } callSite) {
            _registerSpeculativeTarget(callSite, target);
        }
    }


    /// <summary>
    /// Sets the address to DefaultAddressValue if not null. If null does nothing
    /// </summary>
    public void SetAddressToDefault() {
        if (DefaultAddress is null) {
            return;
        }

        SetAddress(DefaultAddress.Value.Segment, DefaultAddress.Value.Offset);
    }

    /// <summary>
    /// Registers the pointer's current target with the CFG for speculative linking, as
    /// <see cref="SetAddress"/> would. Called explicitly once the call instruction exists in the
    /// graph (after CFG reload and known-safe handler seeding): a pointer written before the call
    /// site was discovered leaves no edge otherwise.
    /// </summary>
    public void RegisterCurrentCallTarget() {
        if (PhysicalLocation is not { } location || PatchedCallAddress is not { } callSite) {
            return;
        }
        _registerSpeculativeTarget(callSite, _memory.SegmentedAddress16[location]);
    }
}
