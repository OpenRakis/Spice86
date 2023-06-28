namespace Spice86.Core.Emulator.InterruptHandlers.Common.Callback;

using Spice86.Core.Emulator.InterruptHandlers.Common.IndexBasedDispatcher;
using Spice86.Core.Emulator.ReverseEngineer;

/// <summary>
/// The interface for classes that can be called from ASM.
/// This is done via a special Assembly machine instruction (FE38).
/// </summary>
public interface ICallback : IRunnable {
    /// <summary>
    /// Defines the callback number. For example, 0x2F for DOS Interrupt Handler 2F implementation.
    /// </summary>
    public byte Index { get; }

    /// <summary>
    /// Invoked when interruptions are invoked by machine code overrides. See <see cref="CSharpOverrideHelper"/>
    /// </summary>
    public void RunFromOverriden();

    /// <summary>
    /// Address where the instruction is installed in memory
    /// </summary>
    public uint InstructionPhysicalAddress { get; }
}