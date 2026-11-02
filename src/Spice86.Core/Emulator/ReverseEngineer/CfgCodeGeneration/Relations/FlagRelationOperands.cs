namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Relations;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>
/// The expressions a flag relation can still read at the use: the setter operands as they were before the
/// setter ran, and the value the setter wrote. A null member is not available.
/// </summary>
internal readonly record struct FlagRelationOperands(ValueNode? Left, ValueNode? Right, ValueNode? Result);
