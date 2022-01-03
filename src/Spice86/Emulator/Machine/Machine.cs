namespace Spice86.Emulator.Machine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Emulator.Cpu;

using Spice86.Emulator.Callback;
using Spice86.Emulator.Memory;

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

    public MachineBreakpoints GetMachineBreakpoints()
    {
        return _machineBreakpoints;
    }

    public Cpu GetCpu()
    {
        return _cpu;
    }

    internal Memory GetMemory()
    {
        throw new NotImplementedException();
    }

    internal CallbackHandler GetCallbackHandler()
    {
        throw new NotImplementedException();
    }

    internal object DumpCallStack()
    {
        throw new NotImplementedException();
    }
}
