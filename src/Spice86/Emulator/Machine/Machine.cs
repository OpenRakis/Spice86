namespace Spice86.Emulator.Machine;

using Emulator.Cpu;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Memory;

using System;

/// <summary>
/// Emulates an IBM PC
/// TODO: complete it !
/// </summary>
public class Machine
{
    private readonly Cpu _cpu;

    private readonly MachineBreakpoints _machineBreakpoints;

    public Machine(bool debugMode)
    {
        _cpu = new(this, debugMode);
        _machineBreakpoints = new(this);
    }

    public Cpu GetCpu()
    {
        return _cpu;
    }

    public MachineBreakpoints GetMachineBreakpoints()
    {
        return _machineBreakpoints;
    }

    internal object DumpCallStack()
    {
        throw new NotImplementedException();
    }

    internal CallbackHandler GetCallbackHandler()
    {
        throw new NotImplementedException();
    }

    internal Memory GetMemory()
    {
        throw new NotImplementedException();
    }
}