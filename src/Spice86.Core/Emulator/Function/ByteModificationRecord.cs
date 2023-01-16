namespace Spice86.Core.Emulator.Function;

/// <summary>
/// record: Immutable type, where Equals compares properties values (not references), and GetHashCode is calculated on the given properties.
/// </summary>
/// <param name="OldValue">OldValue property</param>
/// <param name="NewValue">NewValue property</param>
public record ByteModificationRecord(byte OldValue, byte NewValue);