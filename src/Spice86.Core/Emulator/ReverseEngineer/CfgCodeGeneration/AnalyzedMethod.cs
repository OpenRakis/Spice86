namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

/// <summary>
/// A prepared method with the program-wide results it prints with: its typed signature, parameter
/// reads and register-local plan, and <see cref="Body"/> the body after the DOS comments, jump
/// tables and register write-backs.
/// </summary>
/// <param name="RemovedSaves">The relaxed save pairs this typed method dropped, with their push
/// sites for the differential exclusion; empty for an untyped method or without the
/// abandoned-slots relaxation.</param>
/// <param name="Ssa">The SSA over <see cref="PreparedMethod.IrMethod"/> after the removed save
/// pairs left it: rebound when a pair was removed, <see cref="PreparedMethod.Ssa"/> otherwise.</param>
internal sealed record AnalyzedMethod(PreparedMethod Prepared, TypedSignature? Signature,
    IReadOnlyDictionary<IVisitableAstNode, string>? ParameterReads,
    RegisterLocalPlan? RegisterLocals, BlockNode Body,
    IReadOnlyList<SaveRestorePairs.RemovedSave> RemovedSaves, IrSsa Ssa);
