namespace Ix86.Emulator.Devices.Video;

using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;

using System;


[Serializable]
public class InvalidColorIndexException : InvalidVMOperationException
{
    public InvalidColorIndexException(Machine machine, int color) : base(machine, $"Color index {color} is invalid") { }
}
