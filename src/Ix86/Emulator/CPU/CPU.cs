namespace Ix86.Emulator.Cpu;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
///
/// Implementation of a 8086 CPU.<br/>
/// It has some 80186, 80286 and 80386 instructions as some program use them.<br/>
/// It also has some x87 FPU instructions to support telling the programs that x87 is not supported :)<br/>
/// Some docs that helped the implementation:
/// <ul>
/// <li>Instructions decoding: http://rubbermallet.org/8086%20notes.pdf and http://ref.x86asm.net/coder32.html</li>
/// <li>Instructions implementation details: https://www.felixcloutier.com/x86/</li>
/// <li>Pure 8086 instructions: https://jbwyatt.com/253/emu/8086_instruction_set.html</li>
/// </ul>
/// TODO
/// </summary>
public class CPU
{
    public object GetState()
    {
        throw new NotImplementedException();
    }
}
