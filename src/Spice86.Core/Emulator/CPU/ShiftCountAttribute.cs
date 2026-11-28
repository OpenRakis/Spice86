namespace Spice86.Core.Emulator.CPU;

/// <summary>
/// Marks the count operand of a shift or rotate: an 8-bit x86 operand whatever its C# type.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ShiftCountAttribute : Attribute {
}
