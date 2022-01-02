namespace Ix86.Emulator.Machine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Emulator.Cpu;

using Ix86.Emulator.Memory;

public class Machine
{
    private readonly Cpu _cpu = new();

    public Cpu GetCpu()
    {
        return _cpu;
    }

    internal Memory? GetMemory()
    {
        throw new NotImplementedException();
    }
}
