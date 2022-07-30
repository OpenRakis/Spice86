namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;

using System;

[Serializable]
public class InvalidModeException : InvalidVMOperationException {

    public InvalidModeException(Machine machine, int mode) : base(machine, $"Invalid mode {ConvertUtils.ToHex((uint)mode)}") {
    }
}