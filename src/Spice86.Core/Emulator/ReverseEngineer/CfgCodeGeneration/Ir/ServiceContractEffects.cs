namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>
/// The <see cref="IrEffects"/> a typed interrupt service call has, from its
/// <see cref="InterruptServiceEntry"/>. On the stub path the helper stores every register its
/// parameters carry and the selector constant into the CPU state before it runs the stub, so
/// those writes are certain; on the direct path the implementation receives its inputs as C#
/// arguments and stores nothing, so only the result targets are must-written. Both paths read
/// what the service reads minus the supplied registers, plus the current value of every result
/// target (the contract only may-writes the outputs, so a path preserving one reads its
/// pre-call value), plus SS, SP and every flag (the <c>int</c> frame push reads them); they may
/// write what the service writes, and touch memory always (frame and <c>iret</c>) and I/O ports
/// when the service does. The call assigns its return to the result targets, so they are
/// must-written even though the handler itself only may-writes them. The call delivers no
/// hardware interrupt: the <c>int</c> frame clears IF and no precise service sets it again, so
/// the registers a delivered handler could touch are not part of the effect (delivery stays on
/// the separate event-check statement).
/// </summary>
internal static class ServiceContractEffects {
    private const CpuValues FrameReads =
        CpuValues.Ss | CpuValues.Sp | CpuValues.Cs | CpuValues.StatusFlags | CpuValues.Direction;

    /// <summary>The effects of <paramref name="node"/>, without the reads of its printed
    /// arguments (the caller adds them, the way it does for every call).</summary>
    public static IrEffects Of(InterruptServiceCallNode node) {
        ServiceContract contract = node.Contract;
        CpuValues parameterMask = ServiceCallSignature.Parameters(contract, node.Selector)
            .Aggregate(CpuValues.None, (mask, parameter) => mask | parameter.Mask);
        CpuValues selector = node.Selector?.ToCpuValues() ?? CpuValues.None;
        CpuValues resultMask = node.Results.Aggregate(CpuValues.None,
            (mask, result) => mask | IrEffectsVisitor.Of(result, false).Reads);
        CpuValues supplied = parameterMask | selector;
        CpuValues reads = ((contract.Registers.Reads | resultMask) & ~supplied) | FrameReads;
        CpuValues mustWrite = node.Service.ExecutionPath == ServiceExecutionPath.Direct
            ? resultMask
            : supplied | resultMask;
        CpuValues mayWrite = contract.Registers.Writes;
        IrTokens tokens = contract.Io ? IrTokens.All : IrTokens.Memory;
        return new IrEffects(reads, mustWrite, mayWrite, tokens, tokens, false, false);
    }
}
