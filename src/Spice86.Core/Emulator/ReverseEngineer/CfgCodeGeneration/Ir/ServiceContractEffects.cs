namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>
/// The <see cref="IrEffects"/> a typed interrupt service call has, from its
/// <see cref="ServiceContract"/>. The call reads what the service reads minus the selector
/// register (the helper writes it itself), plus SS, SP and every flag (the <c>int</c> frame push
/// reads them); it writes the selector register on every path, may write what the service writes,
/// and touches memory always (frame and <c>iret</c>) and I/O ports when the service does. The
/// call delivers no hardware interrupt: the <c>int</c> frame clears IF and no precise service
/// sets it again, so the registers a delivered handler could touch are not part of the effect
/// (delivery stays on the separate event-check statement).
/// </summary>
internal static class ServiceContractEffects {
    private const CpuValues FrameReads =
        CpuValues.Ss | CpuValues.Sp | CpuValues.Cs | CpuValues.StatusFlags | CpuValues.Direction;

    /// <summary>The effects of <paramref name="node"/>, without the reads of its printed
    /// arguments (the caller adds them, the way it does for every call).</summary>
    public static IrEffects Of(InterruptServiceCallNode node) {
        ServiceContract contract = node.Contract;
        CpuValues selector = node.Selector?.ToCpuValues() ?? CpuValues.None;
        CpuValues reads = (contract.Registers.Reads & ~selector) | FrameReads;
        CpuValues mustWrite = selector;
        CpuValues mayWrite = contract.Registers.Writes;
        IrTokens tokens = contract.Io ? IrTokens.All : IrTokens.Memory;
        return new IrEffects(reads, mustWrite, mayWrite, tokens, tokens, false, false);
    }
}
