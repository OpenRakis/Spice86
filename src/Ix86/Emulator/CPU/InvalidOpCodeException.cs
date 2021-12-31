namespace Ix86.Emulator.Cpu;

using Ix86.Emulator.Errors;
using Ix86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ix86.Emulator.Machine;

[Serializable]
public class InvalidOpCodeException : InvalidVMOperationException
{
    public InvalidOpCodeException(Machine machine, int opcode, bool prefixNotAllowed) : base(machine, GenerateMessage(opcode, prefixNotAllowed))
    {
    }

    private static string GenerateMessage(int opcode, bool prefixNotAllowed)
    {
        return $"opcode={ConvertUtils.ToHex(opcode)}{(prefixNotAllowed ? " prefix is not allowed here" : "")}";
    }
}
