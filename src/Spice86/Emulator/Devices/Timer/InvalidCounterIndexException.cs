namespace Spice86.Emulator.Devices.Timer;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Machine;

using System;

[Serializable]
public class InvalidCounterIndexException : InvalidVMOperationException {

    public InvalidCounterIndexException(Machine machine, int counterIndex) : base(machine, $"Invalid counter index {counterIndex}") {
    }
}