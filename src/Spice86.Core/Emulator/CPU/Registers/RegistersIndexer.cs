namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.Indexer;

using System.Numerics;

/// <summary>
/// Accessor for registers accessible via index.
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class RegistersIndexer<T> : Indexer<T>
    where T : IUnsignedNumber<T> {
}