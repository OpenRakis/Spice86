namespace Ix86.Emulator.Machine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Emulator.Cpu;

public class Machine
{
    private CPU _cpu = new CPU();

    public CPU GetCpu()
    {
        return _cpu;
    }
}
