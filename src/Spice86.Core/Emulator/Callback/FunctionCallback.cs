namespace Spice86.Core.Emulator.Callback;

using Spice86.Core.Emulator.CPU;

public class FunctionCallback : Callback {
    public FunctionCallback(byte index, Action runnable, Registers savedRegisters) : base(index, runnable) {
        
    }
}