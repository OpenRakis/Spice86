namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Cpu;
using Ix86.Emulator.Machine;

public class MemoryBasedDataStructureWithSegmentRegisterBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider
{
    private readonly SegmentRegisters _segmentRegisters;
    private readonly int _segmentRegisterIndex;
    public MemoryBasedDataStructureWithSegmentRegisterBaseAddress(Machine machine, int segmentRegisterIndex) : base(machine.GetMemory())
    {
        this._segmentRegisterIndex = segmentRegisterIndex;
        this._segmentRegisters = machine.GetCpu().GetState().GetSegmentRegisters();
    }

    public override int GetBaseAddress()
    {
        return _segmentRegisters.GetRegister(_segmentRegisterIndex) * 0x10;
    }
}
