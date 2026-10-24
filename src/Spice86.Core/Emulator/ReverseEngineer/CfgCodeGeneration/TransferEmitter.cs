namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Decides how to jump from one place to another in the generated code. Given a CFG edge (source → target),
/// it picks the right C# mechanism: a <c>goto</c> within the same method, a <c>return OtherMethod(...)</c>
/// for cross-partition flow, or a cyclic-flow dispatcher bounce. It only produces code-generation nodes — the
/// actual writing to source text happens elsewhere.
/// </summary>
internal sealed class TransferEmitter(CfgGeneratorContext context) {
    public IVisitableAstNode Emit(ResolvedCfgEdge edge, bool allowCallOutContinuation = false) {
        CfgCodePartition sourcePartition = context.GetPartition(edge.Source);
        CfgCodePartition targetPartition = context.GetPartition(edge.Target);
        if (edge.PartitionTransferKind == CfgCodePartitionTransferKind.CpuFault && sourcePartition == targetPartition) {
            throw new NotSupportedException($"CPU fault transfer from {edge.Source.Address} to {edge.Target.Address} cannot be lowered as same-method control flow.");
        }

        if (sourcePartition == targetPartition) {
            return new GotoNode(edge.Target);
        }

        CfgCodePartitionTransfer? transfer = context.FindTransfer(edge);
        if (transfer is null) {
            throw new NotSupportedException($"Missing partition transfer metadata for edge {edge.Source.Address} -> {edge.Target.Address}.");
        }

        string methodName = context.GetMethodName(targetPartition);
        int loadOffset = context.GetEntryLoadOffset(targetPartition, edge.Target);
        switch (transfer.Kind) {
            case CfgCodePartitionTransferKind.CrossPartitionFlow:
                return PartitionReturn(methodName, loadOffset);
            case CfgCodePartitionTransferKind.CpuFault:
                // The fault-specific work (push flags/return address, clear InterruptFlag, set CS/IP) already
                // ran in the catch block before this transfer, so entering the handler partition is a normal
                // partition entry.
                return PartitionReturn(methodName, loadOffset);
            case CfgCodePartitionTransferKind.CyclicCrossPartitionFlow:
                return new CyclicJumpNode(methodName, loadOffset);
            case CfgCodePartitionTransferKind.CallOut:
                if (allowCallOutContinuation) {
                    return PartitionReturn(methodName, loadOffset);
                }
                throw new NotSupportedException($"Call-out transfer from {edge.Source.Address} to {edge.Target.Address} must be lowered through a generated call helper.");
            case CfgCodePartitionTransferKind.AlignedReturn:
            case CfgCodePartitionTransferKind.DynamicReturn:
                throw new NotSupportedException($"Return transfer from {edge.Source.Address} to {edge.Target.Address} must be lowered through a generated return helper.");
            default:
                throw new NotSupportedException($"Partition transfer kind {transfer.Kind} from {edge.Source.Address} to {edge.Target.Address} is not implemented yet.");
        }
    }

    /// <summary>
    /// Lowers the fallthrough of a non-terminating instruction or callback: its observed <see cref="InstructionSuccessorType.Normal"/> successor at the next address in memory, or a <c>FailAsUntested</c> throw when discovery never observed it falling through (it only faulted, or discovery stopped on it).
    /// A callback whose handler moved CS:IP elsewhere (DOS EXEC, terminate) also has <see cref="InstructionSuccessorType.Normal"/> successors at those targets; those are dispatched over by the callback lowering itself, and this fallthrough is what runs when the handler kept CS:IP at the callback.
    /// </summary>
    public IVisitableAstNode EmitFallthroughIfNeeded(CfgInstruction source) {
        if (source.UniqueSuccessor is not null) {
            return Emit(new ResolvedCfgEdge(source, source.UniqueSuccessor, InstructionSuccessorType.Normal,
                context.FindTransfer(source, source.UniqueSuccessor, InstructionSuccessorType.Normal)?.Kind));
        }

        if (context.TryResolveEdge(source, InstructionSuccessorType.Normal,
                source.NextInMemoryAddress32.ToSegmentedAddress()) is ResolvedCfgEdge fallthrough) {
            return Emit(fallthrough);
        }
        return UntestedMessages.FallthroughAfter(source.Address);
    }

    /// <summary>
    /// Lowers a generated call helper using the statically-known expected return address, then lowers the
    /// observed post-call continuation. The helper (NearCall / FarCall) preserves emulated stack behavior and
    /// validates the actual return target against the pushed return address.
    /// </summary>
    public IVisitableAstNode EmitCallHelperAndContinuation(string helperName, CfgInstruction call, MethodGroupNode function, ushort? farCallTargetCs = null) {
        CallContinuation continuation = context.ResolveCallContinuation(call);
        return CodeGenerationAst.Sequence(
            CallHelper(helperName, continuation.ExpectedReturnAddress, function, farCallTargetCs),
            EmitPostCallContinuation(call, continuation));
    }

    /// <summary>
    /// Builds the generated call-helper invocation shared by near and far call lowering:
    /// <c>Helper(returnSegment, 0xReturnOffset[, targetCs], function);</c>. A far call passes the
    /// callee segment as <paramref name="targetCs"/>; a near call leaves it <c>null</c> and the argument is
    /// omitted.
    /// </summary>
    public MethodCallNode CallHelper(string helperName, SegmentedAddress expectedReturn, MethodGroupNode function, ushort? targetCs) {
        List<IVisitableAstNode> arguments = [
            new SegmentVariableNode(expectedReturn.Segment),
            new OffsetLiteralNode(expectedReturn.Offset)
        ];
        if (targetCs is not null) {
            arguments.Add(new SegmentVariableNode(targetCs.Value));
        }
        arguments.Add(function);
        return new MethodCallNode(null, helperName, arguments.ToArray());
    }

    /// <summary>
    /// Lowers control flow after a call helper returns. When the callee returned during discovery the observed
    /// typed continuation edge is lowered as the post-call continuation. When no continuation was observed
    /// (the callee did not return during discovery, e.g. it exits the program), the post-call path is unobserved,
    /// so generated code fails as untested if it is ever reached.
    /// </summary>
    public IVisitableAstNode EmitPostCallContinuation(CfgInstruction call, CallContinuation continuation) {
        if (continuation.ObservedContinuationEdge is ResolvedCfgEdge edge) {
            return Emit(edge, allowCallOutContinuation: true);
        }

        return UntestedMessages.ReturnFromCall(call.Address);
    }

    /// <summary>
    /// Builds the function a generated call helper invokes for a resolved target edge, accounting for a
    /// non-primary entry's <c>loadOffset</c>.
    /// </summary>
    public MethodGroupNode FunctionReference(ResolvedCfgEdge targetEdge) {
        CfgCodePartition targetPartition = context.GetPartition(targetEdge.Target);
        string methodName = context.GetMethodName(targetPartition);
        int loadOffset = context.GetEntryLoadOffset(targetPartition, targetEdge.Target);
        return new MethodGroupNode(methodName, loadOffset);
    }

    /// <summary>
    /// Builds the direct invocation for a partition transfer edge of kind <see cref="CfgCodePartitionTransferKind.CpuFault"/> or <see cref="CfgCodePartitionTransferKind.CrossPartitionFlow"/>.
    /// </summary>
    /// <param name="edge">The resolved CFG edge with validated transfer metadata.</param>
    /// <returns>The method entered at the load offset, e.g., <c>MethodName(0x0000)</c>.</returns>
    /// <exception cref="NotSupportedException">Thrown when the edge is a same-partition CPU fault, missing transfer metadata, or has an unsupported transfer kind.</exception>
    public CpuFaultHandler FaultHandler(ResolvedCfgEdge edge) {
        CfgCodePartition sourcePartition = context.GetPartition(edge.Source);
        CfgCodePartition targetPartition = context.GetPartition(edge.Target);
        if (sourcePartition == targetPartition) {
            throw new NotSupportedException($"CPU fault transfer from {edge.Source.Address} to {edge.Target.Address} cannot be lowered as same-method control flow.");
        }

        CfgCodePartitionTransfer? transfer = context.FindTransfer(edge);
        if (transfer is null) {
            throw new NotSupportedException($"Missing partition transfer metadata for edge {edge.Source.Address} -> {edge.Target.Address}.");
        }

        if (transfer.Kind != CfgCodePartitionTransferKind.CpuFault && transfer.Kind != CfgCodePartitionTransferKind.CrossPartitionFlow) {
            throw new NotSupportedException($"Partition transfer kind {transfer.Kind} from {edge.Source.Address} to {edge.Target.Address} is not supported for direct partition call lowering.");
        }

        string methodName = context.GetMethodName(targetPartition);
        int loadOffset = context.GetEntryLoadOffset(targetPartition, edge.Target);
        return new CpuFaultHandler(edge.Target.Address, methodName, loadOffset);
    }

    private static IVisitableAstNode PartitionReturn(string methodName, int loadOffset) =>
        new ReturnNode(new MethodCallNode(null, methodName, new OffsetLiteralNode(loadOffset)));
}
