namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

using System.Collections.Generic;

/// <summary>
/// Stack-based frontier for depth-first traversal.
/// </summary>
public sealed class StackFrontier<T> : IFrontier<T> {
    private readonly Stack<T> _stack = new();

    /// <inheritdoc />
    public int Count => _stack.Count;

    /// <inheritdoc />
    public void Add(T item) => _stack.Push(item);

    /// <inheritdoc />
    public T Remove() => _stack.Pop();
}
