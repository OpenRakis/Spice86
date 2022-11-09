namespace Spice86.Core.Emulator.Devices.Timer;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;

using System;

[Serializable]
public class InvalidCounterIndexException : InvalidVMOperationException {
    public InvalidCounterIndexException(Machine machine, int counterIndex) : base(machine, $"Invalid counter index {counterIndex}") {
    }
}