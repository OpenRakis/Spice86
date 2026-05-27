namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

/// <summary>
/// Abstraction over the data structure that determines traversal order.
/// </summary>
public interface IFrontier<T> {
    /// <summary>Number of elements in the frontier.</summary>
    int Count { get; }

    /// <summary>Adds an element to the frontier.</summary>
    void Add(T item);

    /// <summary>Removes and returns the next element from the frontier.</summary>
    T Remove();
}
