namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Handles writing and switching SegmentedAddress in emulated memory
/// </summary>
public class InMemoryAddressSwitcher {
    private readonly IIndexable _memory;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAddressSwitcher"/> class.
    /// </summary>
    /// <param name="memory">The memory bus</param>
    public InMemoryAddressSwitcher(IIndexable memory) {
        _memory = memory;
    }

    /// <summary>
    /// Physical address of the SegmentedAddress we want to store.
    /// </summary>
    public uint? PhysicalLocation { private get; set; }

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
        // Write the address
        _memory.SegmentedAddress[PhysicalLocation.Value] = new(segment, offset);
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
}