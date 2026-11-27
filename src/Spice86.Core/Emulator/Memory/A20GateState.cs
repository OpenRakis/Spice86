namespace Spice86.Core.Emulator.Memory;

/// <summary>
///     State of the A20 line, as captured by <see cref="A20Gate.CaptureState" />.
/// </summary>
/// <param name="IsEnabled">Whether the 20th address line is enabled.</param>
public readonly record struct A20GateState(bool IsEnabled);
