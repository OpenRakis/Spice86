namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Memory;

public class MemoryBasedDataStructureWithBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider {
    private readonly uint _baseAddress;

    public MemoryBasedDataStructureWithBaseAddress(Memory memory, uint baseAddress) : base(memory) {
        _baseAddress = baseAddress;
    }

    public override uint BaseAddress => _baseAddress;
}