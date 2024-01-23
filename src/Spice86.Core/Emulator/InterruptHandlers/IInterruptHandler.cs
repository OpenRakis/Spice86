namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

/// <summary>
/// Interface for C# interrupt handlers. <br/>
/// Interrupt handlers write their ASM code in emulated memory.
/// </summary>
public interface IInterruptHandler : IAssemblyRoutineWriter {
    /// <summary>
    /// Vector number of the interrupt being represented
    /// </summary>
    public byte VectorNumber { get; }
}