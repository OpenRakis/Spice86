namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.CPU.CfgCpu.Feeder;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles writing and switching SegmentedAddress in emulated memory
/// </summary>
public class InMemoryAddressSwitcher {
    private readonly IIndexable _memory;
    private readonly ICallTargetRegistry _callTargetRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAddressSwitcher"/> class.
    /// </summary>
    /// <param name="memory">The memory bus</param>
    /// <param name="callTargetRegistry">Notified with <c>(call site, target)</c>
    /// each time the far pointer of a switchable call is written, so the CFG can speculatively link
    /// the call instruction to its new target.</param>
    public InMemoryAddressSwitcher(IIndexable memory,
        ICallTargetRegistry callTargetRegistry) {
        _memory = memory;
        _callTargetRegistry = callTargetRegistry;
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
            _callTargetRegistry.RegisterCallTarget(callSite, target);
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
    /// The switchable call this switcher drives: the emitted call site and the target the pointer
    /// holds in memory now, or null while the pointer location or the call site is unset.
    /// </summary>
    public SwitchableCall? CurrentCall {
        get {
            if (PhysicalLocation is not { } location || PatchedCallAddress is not { } callSite) {
                return null;
            }
            return new SwitchableCall(callSite, _memory.SegmentedAddress16[location]);
        }
    }
}
