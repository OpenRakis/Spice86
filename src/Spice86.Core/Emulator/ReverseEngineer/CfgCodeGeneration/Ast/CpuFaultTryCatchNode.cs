namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>An instruction body wrapped in <c>try</c>/<c>catch (CpuException)</c> dispatching to the observed fault handlers.</summary>
/// <param name="Body">The faulting instruction body, run inside the <c>try</c>.</param>
/// <param name="FaultingInstruction">The address of the faulting instruction, reported in the dispatch call.</param>
/// <param name="Handlers">The CPU fault handlers the catch dispatches to, in print order.</param>
internal sealed record CpuFaultTryCatchNode(BlockNode Body, SegmentedAddress FaultingInstruction, IReadOnlyList<CpuFaultHandler> Handlers) : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) => CodeGenerationNode.RequireVisitor(astVisitor, this).VisitCpuFaultTryCatchNode(this);
}
