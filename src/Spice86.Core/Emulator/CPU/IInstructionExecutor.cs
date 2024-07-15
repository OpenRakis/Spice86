namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

/// <summary>
/// The API contract for the CPU for the <see cref="EmulationLoop"/>
/// </summary>
public interface IInstructionExecutor {
    /// <summary>
    /// Executes the next instruction.
    /// </summary>
    public void ExecuteNext();
}