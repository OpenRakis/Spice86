namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

/// <summary>
/// API contract for an executor used by the <see cref="EmulationLoop"/>, typically a CPU
/// </summary>
public interface IInstructionExecutor {
    /// <summary>
    /// Executes the next instruction.
    /// </summary>
    public void ExecuteNext();

    /// <summary>
    /// Signal that we are at the entry point of the program ready to start executing our very first instruction
    /// </summary>
    public void SignalEntry();
    
    /// <summary>
    /// Signal that emulation just stopped, no more instruction will be executed
    /// </summary>
    public void SignalEnd();
}