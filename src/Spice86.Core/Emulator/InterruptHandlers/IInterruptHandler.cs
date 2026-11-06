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
    /// The services the handler exposes on its vector: one service when the whole vector has a
    /// single behavior, or the services the selector register chooses between. The default
    /// implementation builds a <see cref="HandlerServices.SingleService"/> from the parameterless
    /// <see cref="InterruptServiceAttribute"/> of the type, or from
    /// <see cref="InterruptServiceEntry.Unknown"/> when it has none.
    /// </summary>
    public HandlerServices Services {
        get {
            InterruptServiceEntry? entry =
                InterruptServiceAttributes.Of(GetType()).SingleService?.Entry;
            return new HandlerServices.SingleService(entry ?? InterruptServiceEntry.Unknown);
        }
    }

    /// <summary>
    /// Vectors the handler's emulated code itself raises with an <c>int</c> (for example the user
    /// timer hook int 1Ch inside the int 8 stub): a program can hook them, so the interrupt
    /// contract must know their IVT targets.
    /// </summary>
    public IReadOnlySet<byte> ChainedVectorNumbers => FrozenSet<byte>.Empty;
}