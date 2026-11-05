namespace Spice86.Core.Emulator.InterruptHandlers;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.RoutineInstall;

using System.Collections.Frozen;

/// <summary>
/// Interface for C# interrupt handlers. <br/>
/// Interrupt handlers write their ASM code in emulated memory.
/// </summary>
public interface IInterruptHandler : IAssemblyRoutineWriter {
    /// <summary>
    /// Vector number of the interrupt being represented
    /// </summary>
    public byte VectorNumber { get; }

    /// <summary>
    /// The services the handler exposes on its vector: one contract when the whole vector has a
    /// single behavior, or the contracts the selector register chooses between. The default
    /// implementation builds a <see cref="HandlerServices.SingleService"/> from the parameterless
    /// <see cref="InterruptServiceAttribute"/> of the type, or from
    /// <see cref="ServiceContract.Unknown"/> when it has none.
    /// </summary>
    public HandlerServices Services {
        get {
            ServiceContract? contract = InterruptServiceAttributes.Of(GetType()).SingleContract;
            return new HandlerServices.SingleService(contract ?? ServiceContract.Unknown);
        }
    }

    /// <summary>
    /// Vectors the handler's emulated code itself raises with an <c>int</c> (for example the user
    /// timer hook int 1Ch inside the int 8 stub): a program can hook them, so the interrupt
    /// contract must know their IVT targets.
    /// </summary>
    public IReadOnlySet<byte> ChainedVectorNumbers => FrozenSet<byte>.Empty;
}