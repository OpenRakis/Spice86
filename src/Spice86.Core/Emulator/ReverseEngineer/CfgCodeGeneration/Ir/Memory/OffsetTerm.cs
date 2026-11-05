namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>One term of a normalized offset: a register and the SSA version it is read at.</summary>
internal sealed record OffsetTerm(RegisterNode Register, int Version);
