namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

/// <summary>
/// A prepared method with the program-wide results it prints with: its typed signature, parameter
/// reads and register-local plan, and <see cref="Body"/> the body after the DOS comments, jump
/// tables and register write-backs.
/// </summary>
internal sealed record AnalyzedMethod(PreparedMethod Prepared, TypedSignature? Signature,
    IReadOnlyDictionary<IVisitableAstNode, string>? ParameterReads, RegisterLocalPlan? RegisterLocals, BlockNode Body);
