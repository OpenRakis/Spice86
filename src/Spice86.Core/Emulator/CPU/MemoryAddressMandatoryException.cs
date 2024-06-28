namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.Errors;

public class MemoryAddressMandatoryException : InvalidVMOperationException {
    public MemoryAddressMandatoryException(State state) : base(state,
        "Memory address is mandatory for this instruction") {
    }
}