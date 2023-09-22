namespace Spice86.Core.Emulator.Memory.ReaderWriter; 

/// <summary>
/// Implementation of IReaderWriter on top of an array of type T
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class ArrayReaderWriter<T>: IReaderWriter<T> {
    public T[] Array { get; }

    public ArrayReaderWriter(T[] array) {
        Array = array;
    }

    /// <inheritdoc/>
    public T this[uint address] {
        get => Array[address];
        set => Array[address] = value;
    }

    /// <inheritdoc/>
    public uint Length { get => (uint)Array.Length; }
}