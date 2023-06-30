namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

/// <summary>
/// Represents a memory base structure with a base address.
/// </summary>
public class MemoryBasedDataStructureWithBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider {
    private readonly uint _baseAddress;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="baseAddress">The base address of the data structure.</param>
    public MemoryBasedDataStructureWithBaseAddress(IMemory memory, uint baseAddress) : base(memory) {
        _baseAddress = baseAddress;
    }

    /// <summary>
    /// The base address of the data structure.
    /// </summary>
    public override uint BaseAddress => _baseAddress;
}