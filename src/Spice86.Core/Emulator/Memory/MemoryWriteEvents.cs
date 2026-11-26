namespace Spice86.Core.Emulator.Memory;

using Spice86.Shared.Emulator.Memory;

using System.Runtime.CompilerServices;

/// <summary>
/// Holds the <see cref="IMemoryWriteObserver"/> the memory indexers report their writes to. The memory bus
/// creates one and gives it to its indexers.
/// </summary>
public sealed class MemoryWriteEvents {
    /// <summary>
    /// The instance of the indexers over memory other than the bus (byte arrays, memory based data
    /// structures). Nothing sets its observer, so they report no write.
    /// </summary>
    internal static MemoryWriteEvents Unobserved { get; } = new();

    internal MemoryWriteEvents() {
    }

    /// <summary>
    /// The observer told about every write, or <see langword="null"/> (the default) for none. See
    /// <see cref="IMemoryWriteObserver"/> for what raises an event. Set it on the emulation thread, or while
    /// the emulation is paused.
    /// </summary>
    public IMemoryWriteObserver? Observer { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Raise(uint address, BitWidth width, uint value) {
        IMemoryWriteObserver? observer = Observer;
        if (observer is not null) {
            observer.OnMemoryWrite(new MemoryWrite(address, width, value));
        }
    }
}
