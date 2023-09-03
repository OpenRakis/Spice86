namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

using System;

[Serializable]
public class InvalidModeException : InvalidVMOperationException {
    public InvalidModeException(ICpuState state, int mode) : base(state, $"Invalid mode {ConvertUtils.ToHex((uint)mode)}") {
    }
}