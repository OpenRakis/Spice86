namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;

/// <summary>An <see cref="AluExpansion"/> after <see cref="AluExpansionCleanup"/>.</summary>
/// <param name="Statements">The remaining temp declarations, then the remaining flag assignments, in execution order.</param>
/// <param name="Value">The value of the call, to evaluate where the call was after <paramref name="Statements"/>;
/// <c>null</c> when the caller does not use the value.</param>
internal sealed record CleanedAluExpansion(IReadOnlyList<IVisitableAstNode> Statements, ValueNode? Value);
