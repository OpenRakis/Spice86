namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>
/// One <c>private static readonly byte?[]</c> field of the generated override class: the instruction whose
/// signature bytes it holds, the field name, and those bytes.
/// </summary>
internal sealed record SignatureFieldPlan(CfgInstruction Instruction, string FieldName, IReadOnlyList<byte?> Bytes);
