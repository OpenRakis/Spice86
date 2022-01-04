namespace Spice86.Emulator.ReverseEngineer;

using Spice86.Emulator.Cpu;
using Spice86.Emulator.Machine;

public class MemoryBasedDataStructureWithSegmentRegisterBaseAddress : MemoryBasedDataStructureWithBaseAddressProvider
{
    private readonly int _segmentRegisterIndex;

    private readonly SegmentRegisters _segmentRegisters;

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