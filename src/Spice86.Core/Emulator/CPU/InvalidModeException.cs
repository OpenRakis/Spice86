namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

using System;

[Serializable]
public class InvalidModeException : InvalidVMOperationException {
    public InvalidModeException(State state, int mode) : base(state, $"Invalid mode {ConvertUtils.ToHex((uint)mode)}") {
    }

    public InvalidModeException(State state, string message) : base(state, message) {
    }

    public InvalidModeException(State state, Exception e) : base(state, e) {
    }
}