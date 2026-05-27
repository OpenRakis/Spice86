namespace Spice86.Core.Emulator.Function;

/// <summary>
/// Describes a call stack frame matching an observed return address.
/// </summary>
public readonly record struct FunctionCallStackMatch(int Index, FunctionCall FunctionCall, bool IsTop);