namespace Spice86.Core.Emulator.Function;

/// <summary>
/// An immutable representation of a byte change.
/// </summary>
/// <param name="OldValue">The old byte value.</param>
/// <param name="NewValue">The new byte value.</param>
public readonly record struct ByteModificationRecord(byte OldValue, byte NewValue);