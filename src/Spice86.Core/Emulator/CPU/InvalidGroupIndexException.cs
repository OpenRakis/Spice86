namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Utils;

using System;

[Serializable]
public class InvalidGroupIndexException : InvalidVMOperationException {
    public InvalidGroupIndexException(Machine machine, int groupIndex) : base(machine, $"Invalid group index {ConvertUtils.ToHex((uint)groupIndex)}") {
    }
}