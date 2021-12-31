namespace Ix86.Emulator.CPU;

using Ix86.Emulator.Errors;
using Ix86.Emulator.Machine;
using Ix86.Utils;

using System;


[Serializable]
public class InvalidModeException : InvalidVMOperationException
{
    public InvalidModeException(Machine machine, int mode) : base(machine, $"Invalid mode {ConvertUtils.ToHex(mode)}")
    {
    }
}
