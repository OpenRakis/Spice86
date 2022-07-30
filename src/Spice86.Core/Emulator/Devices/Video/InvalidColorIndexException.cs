namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;

using System;

[Serializable]
public class InvalidColorIndexException : InvalidVMOperationException {

    public InvalidColorIndexException(Machine machine, int color) : base(machine, $"Color index {color} is invalid") {
    }
}