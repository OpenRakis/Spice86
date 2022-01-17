namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Utils;

using System;

[Serializable]
public class InvalidOpCodeException : InvalidVMOperationException {

    public InvalidOpCodeException(Machine machine, int opcode, bool prefixNotAllowed) : base(machine, GenerateMessage(opcode, prefixNotAllowed)) {
    }

    private static string GenerateMessage(int opcode, bool prefixNotAllowed) {
        return $"opcode={ConvertUtils.ToHex(opcode)}{(prefixNotAllowed ? " prefix is not allowed here" : "")}";
    }
}