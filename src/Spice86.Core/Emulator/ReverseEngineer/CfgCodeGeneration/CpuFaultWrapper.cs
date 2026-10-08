namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;
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
    public EmittedCode Wrap(CfgInstruction instruction, EmittedCode body, MethodPlan method) {
        IReadOnlyList<ResolvedCfgEdge> faultEdges = context.GetSuccessorEdges(instruction, InstructionSuccessorType.CpuFault);
        if (faultEdges.Count == 0) {
            return body;
        }

        var orderedEdges = faultEdges
            .OrderBy(e => e.Target.Address.Segment)
            .ThenBy(e => e.Target.Address.Offset)
            .ToList();

        List<string> targetDescriptors = new();
        foreach (ResolvedCfgEdge edge in orderedEdges) {
            string handlerVariable = context.GetSegmentVariable(edge.Target.Address.Segment);
            string callExpression = transferEmitter.PartitionCallExpression(edge);
            targetDescriptors.Add($"new CpuFaultTarget(new SegmentedAddress({handlerVariable}, 0x{edge.Target.Address.Offset:X4}), () => {callExpression})");
        }

        string targetsCollection = string.Join(", ", targetDescriptors);
        string catchLine = $"return DispatchCpuFault(cpuException, {context.GetSegmentVariable(instruction.Address.Segment)}, 0x{instruction.Address.Offset:X4}, [{targetsCollection}]);";

        return EmittedCode.Statements(
            new TryCatchStatement(body.AsStatements(), "catch (CpuException cpuException)",
                [new LineStatement(catchLine, Diverges: true)]));
    }
}