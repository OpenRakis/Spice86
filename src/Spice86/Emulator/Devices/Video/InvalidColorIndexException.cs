namespace Spice86.Emulator.Devices.Video;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM;

using System;

[Serializable]
public class InvalidColorIndexException : InvalidVMOperationException {

    public InvalidColorIndexException(Machine machine, int color) : base(machine, $"Color index {color} is invalid") {
    }
}