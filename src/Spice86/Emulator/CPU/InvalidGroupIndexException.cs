namespace Spice86.Emulator.Cpu;

using Spice86.Emulator.Errors;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spice86.Emulator.Machine;
using Spice86.Utils;

[Serializable]
public class InvalidGroupIndexException : InvalidVMOperationException
{
    public InvalidGroupIndexException(Machine machine, int groupIndex) : base(machine, $"Invalid group index {ConvertUtils.ToHex(groupIndex)}")
    {
    }
}
