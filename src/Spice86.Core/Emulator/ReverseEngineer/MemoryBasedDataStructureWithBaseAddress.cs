namespace Spice86.Core.Emulator.ReverseEngineer;

using Spice86.Core.Emulator.Memory;

public class MemoryBasedDataStructureWithBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider {
    private readonly uint _baseAddress;

    public MemoryBasedDataStructureWithBaseAddress(Memory memory, uint baseAddress) : base(memory) {
        _baseAddress = baseAddress;
    }

    public override uint BaseAddress => _baseAddress;
}