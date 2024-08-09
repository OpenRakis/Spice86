namespace Spice86.Core.Emulator.CPU;

using Spice86.Core.Emulator.VM;

/// <summary>
/// The <see cref="Cpu"/>'s API contract for the <see cref="EmulationLoop"/>
/// </summary>
public interface IInstructionExecutor {
    /// <summary>
    /// Executes the next instruction.
    /// </summary>
    public void ExecuteNext();
}