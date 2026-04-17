namespace Spice86.Core.Emulator.CPU.CfgCpu.Logging;

/// <summary>
/// A named expression compiled into a delegate for zero-interpreter overhead per instruction.
/// </summary>
public record CompiledLogExpression(string Name, Func<uint> Evaluate);
