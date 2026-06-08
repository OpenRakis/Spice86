namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Statement;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Decides how to jump from one place to another in the generated code. Given a CFG edge (source → target),
/// it picks the right C# mechanism: a <c>goto</c> within the same method, a <c>return OtherMethod(...)</c>
/// for cross-partition flow, or a cyclic-flow dispatcher bounce. It only produces code values — the actual
/// writing to source text happens elsewhere.
/// </summary>
internal sealed class TransferEmitter(CfgGeneratorContext context) {
    public EmittedCode Emit(ResolvedCfgEdge edge, MethodPlan methodPlan, bool forceSameMethodGoto = false, bool allowCallOutContinuation = false) {
        CfgCodePartition sourcePartition = context.GetPartition(edge.Source);
        CfgCodePartition targetPartition = context.GetPartition(edge.Target);
        if (edge.PartitionTransferKind == CfgCodePartitionTransferKind.CpuFault && sourcePartition == targetPartition) {
            throw new NotSupportedException($"CPU fault transfer from {edge.Source.Address} to {edge.Target.Address} cannot be lowered as same-method control flow.");
        }

        if (sourcePartition == targetPartition) {
            return EmitSameMethodTransfer(edge.Source, edge.Target, methodPlan, forceSameMethodGoto);
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
                return EmittedCode.Statements(
                    new BlockStatement($"if (JumpDispatcher.Jump({methodName}, 0x{loadOffset:X4}))", [
                        new LineStatement("loadOffset = JumpDispatcher.NextEntryAddress;"),
                        new LineStatement("goto entrydispatcher;", Diverges: true)
                    ]),
                    new LineStatement("return JumpDispatcher.RequiredJumpAsmReturn;", Diverges: true));
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

    public EmittedCode EmitFallthroughIfNeeded(ICfgNode source, MethodPlan methodPlan) {
        if (source.UniqueSuccessor is not null) {
            return Emit(new ResolvedCfgEdge(source, source.UniqueSuccessor, InstructionSuccessorType.Normal,
                context.FindTransfer(source, source.UniqueSuccessor, InstructionSuccessorType.Normal)?.Kind), methodPlan);
        }

        return EmittedCode.None;
    }

    /// <summary>
    /// Lowers a generated call helper using the statically-known expected return address, then lowers the
    /// observed post-call continuation. The helper (NearCall / FarCall) preserves emulated stack behavior and
    /// validates the actual return target against the pushed return address.
    /// </summary>
    public EmittedCode EmitCallHelperAndContinuation(string helperName, CfgInstruction call, string functionExpression, MethodPlan method, ushort? farCallTargetCs = null) {
        CallContinuation continuation = context.ResolveCallContinuation(call);
        string callLine = BuildCallHelperLine(helperName, continuation.ExpectedReturnAddress, functionExpression, farCallTargetCs);
        return EmittedCode.Concat(
            EmittedCode.Line(callLine),
            EmitPostCallContinuation(call, continuation, method));
    }

    /// <summary>
    /// Builds the generated call-helper invocation line shared by near and far call lowering:
    /// <c>Helper(returnSegment, 0xReturnOffset[, targetCs], functionExpression);</c>. A far call passes the
    /// callee segment as <paramref name="targetCs"/>; a near call leaves it <c>null</c> and the argument is
    /// omitted.
    /// </summary>
    public string BuildCallHelperLine(string helperName, SegmentedAddress expectedReturn, string functionExpression, ushort? targetCs) {
        string targetCsArgument = targetCs is null ? string.Empty : $"{context.GetSegmentVariable(targetCs.Value)}, ";
        return $"{helperName}({context.GetSegmentVariable(expectedReturn.Segment)}, 0x{expectedReturn.Offset:X4}, {targetCsArgument}{functionExpression});";
    }

    /// <summary>
    /// Lowers control flow after a call helper returns. When the callee returned during discovery the observed
    /// typed continuation edge is lowered as the post-call continuation. When no continuation was observed
    /// (the callee did not return during discovery, e.g. it exits the program), the post-call path is unobserved,
    /// so generated code fails as untested if it is ever reached.
    /// <para>
    /// <paramref name="forceSameMethodGoto"/> must be set when this is emitted inside a runtime-dispatch
    /// <c>if</c>-branch: a same-method fallthrough would otherwise fall out of the branch into the trailing
    /// untested-target failure instead of reaching the continuation label.
    /// </para>
    /// </summary>
    public EmittedCode EmitPostCallContinuation(CfgInstruction call, CallContinuation continuation, MethodPlan method, bool forceSameMethodGoto = false) {
        if (continuation.ObservedContinuationEdge is ResolvedCfgEdge edge) {
            return Emit(edge, method, forceSameMethodGoto: forceSameMethodGoto, allowCallOutContinuation: true);
        }

        return EmittedCode.Diverging($"throw FailAsUntested(\"Call at {call.Address} returned to {continuation.ExpectedReturnAddress}, but no continuation was observed during discovery.\");");
    }

    /// <summary>
    /// Builds the function-pointer expression a generated call helper invokes for a resolved target edge,
    /// accounting for a non-primary entry's <c>loadOffset</c>.
    /// </summary>
    public string FunctionExpression(ResolvedCfgEdge targetEdge) {
        CfgCodePartition targetPartition = context.GetPartition(targetEdge.Target);
        string methodName = context.GetMethodName(targetPartition);
        int loadOffset = context.GetEntryLoadOffset(targetPartition, targetEdge.Target);
        return loadOffset == 0 ? methodName : $"_ => {methodName}(0x{loadOffset:X4})";
    }

    private EmittedCode EmitSameMethodTransfer(ICfgNode source, ICfgNode target, MethodPlan methodPlan, bool forceGoto) {
        ICfgNode? next = methodPlan.GetNextEmittedNode(source);
        if (!forceGoto && ReferenceEquals(target, next)) {
            return EmittedCode.None;
        }
        return EmittedCode.Diverging($"goto {context.GetLabel(target)};");
    }

    private static EmittedCode PartitionReturn(string methodName, int loadOffset) =>
        EmittedCode.Diverging($"return {methodName}(0x{loadOffset:X4});");
}
