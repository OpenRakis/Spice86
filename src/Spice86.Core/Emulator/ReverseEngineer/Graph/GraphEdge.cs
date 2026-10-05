namespace Spice86.Core.Emulator.ReverseEngineer.Graph;

/// <summary>
/// A directed edge from <see cref="Source"/> to <see cref="Target"/>.
/// </summary>
/// <typeparam name="T">The node type.</typeparam>
/// <param name="Source">The source node.</param>
/// <param name="Target">The target node.</param>
public sealed record GraphEdge<T>(T Source, T Target);
