namespace Spice86.Emulator.CPU;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;
using Spice86.Utils;

using System;

[Serializable]
public class InvalidModeException : InvalidVMOperationException {

    public InvalidModeException(Machine machine, int mode) : base(machine, $"Invalid mode {ConvertUtils.ToHex(mode)}") {
    }
}