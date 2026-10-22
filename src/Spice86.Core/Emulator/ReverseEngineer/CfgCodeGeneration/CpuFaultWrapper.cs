namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer;

using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Wraps an instruction body in <c>try/catch(CpuException)</c> when that instruction was observed to trigger
/// a CPU fault (e.g. divide-by-zero). The catch block delegates to <see cref="CSharpOverrideHelper.DispatchCpuFault"/>
/// which reads the live IVT, matches the handler, performs the fault entry sequence, and invokes the handler partition.
/// Only applied when the instruction actually faulted during discovery; otherwise the body passes through
/// unchanged.
/// </summary>
internal sealed class CpuFaultWrapper(CfgGeneratorContext context, TransferEmitter transferEmitter) {
    /// <summary>
    /// Wraps <paramref name="body"/> in a fault-handling <c>try</c>/<c>catch</c> when the instruction has
    /// observed CPU-fault edges; otherwise returns the body unchanged.
    /// </summary>
    public IVisitableAstNode Wrap(CfgInstruction instruction, IVisitableAstNode body, MethodPlan method) {
        IReadOnlyList<ResolvedCfgEdge> faultEdges = context.GetSuccessorEdges(instruction, InstructionSuccessorType.CpuFault);
        if (faultEdges.Count == 0) {
            return body;
        }

        List<ResolvedCfgEdge> orderedEdges = faultEdges
            .OrderBy(e => e.Target.Address.Segment)
            .ThenBy(e => e.Target.Address.Offset)
            .ToList();

        return new CpuFaultTryCatchNode(CodeGenerationAst.Sequence(body), instruction.Address,
            orderedEdges.Select(transferEmitter.FaultHandler).ToList());
    }
}
