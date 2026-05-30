namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using System.Linq;

/// <summary>
/// Wraps an instruction body in <c>try/catch(CpuException)</c> when that instruction was observed to trigger
/// a CPU fault (e.g. divide-by-zero). The catch block looks up the interrupt handler through the IVT, pushes
/// flags and return address like the real hardware would, then transfers to the handler partition.
/// Only applied when the instruction actually faulted during discovery; otherwise the body passes through
/// unchanged.
/// </summary>
internal sealed class CpuFaultWrapper(CfgGeneratorContext context, TransferEmitter transferEmitter) {
    /// <summary>
    /// Wraps <paramref name="body"/> in a fault-handling <c>try</c>/<c>catch</c> when the instruction has
    /// observed CPU-fault edges; otherwise returns the body unchanged.
    /// </summary>
    public EmittedCode Wrap(CfgInstruction instruction, EmittedCode body, MethodPlan method) {
        IReadOnlyList<ResolvedCfgEdge> faultEdges = context.GetObservedEdges(instruction, InstructionSuccessorType.CpuFault);
        if (faultEdges.Count == 0) {
            return body;
        }

        List<StatementItem> catchBody = [
            new LineStatement("SegmentedAddress cpuFaultTarget = Machine.InterruptVectorTable[cpuException.InterruptVector];")
        ];
        foreach (ResolvedCfgEdge edge in faultEdges) {
            catchBody.Add(new BlockStatement(
                $"if (cpuFaultTarget == new SegmentedAddress({context.GetSegmentVariable(edge.Target.Address.Segment)}, 0x{edge.Target.Address.Offset:X4}))", [
                    new LineStatement($"EnterCpuFaultHandler({context.GetSegmentVariable(instruction.Address.Segment)}, 0x{instruction.Address.Offset:X4}, cpuFaultTarget);"),
                    .. transferEmitter.Emit(edge, method).AsStatements()
                ]));
        }
        catchBody.Add(new LineStatement($"throw FailAsUntested($\"Unknown CPU fault target {{cpuFaultTarget}} at {instruction.Address}\");", Diverges: true));

        return EmittedCode.Statements(
            new BlockStatement("try", body.AsStatements()),
            new BlockStatement("catch (CpuException cpuException)", catchBody));
    }
}
