namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Holds the hidden descriptor cache for each of the six segment registers, indexed by
/// <see cref="SegmentRegisterIndex"/>. Initialized for real-mode operation with a null selector;
/// segment loads (real or protected mode) overwrite the relevant entry.
/// </summary>
public class SegmentDescriptorCaches {
    private readonly SegmentDescriptorCache[] _caches;

    /// <summary>
    /// Initializes a new instance with every segment register defaulting to a real-mode cache for
    /// selector 0.
    /// </summary>
    public SegmentDescriptorCaches() {
        _caches = new SegmentDescriptorCache[6];
        for (int index = 0; index < _caches.Length; index++) {
            _caches[index] = SegmentDescriptorCache.CreateRealMode(0);
        }
    }

    /// <summary>
    /// Gets or sets the descriptor cache for the given segment register.
    /// </summary>
    /// <param name="segmentRegisterIndex">The segment register whose cache to access.</param>
    public SegmentDescriptorCache this[SegmentRegisterIndex segmentRegisterIndex] {
        get => _caches[(int)segmentRegisterIndex];
        set => _caches[(int)segmentRegisterIndex] = value;
    }
}
