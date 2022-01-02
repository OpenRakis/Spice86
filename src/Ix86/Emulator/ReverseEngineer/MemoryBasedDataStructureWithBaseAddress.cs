namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Memory;

public class MemoryBasedDataStructureWithBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider
{
    private readonly int _baseAddress;
    public MemoryBasedDataStructureWithBaseAddress(Memory memory, int baseAddress) : base(memory)
    {
        this._baseAddress = baseAddress;
    }

    public override int GetBaseAddress()
    {
        return _baseAddress;
    }
}
