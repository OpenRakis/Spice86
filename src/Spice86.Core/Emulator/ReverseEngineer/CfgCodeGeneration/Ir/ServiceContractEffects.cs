namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

using System.Linq;

/// <summary>
/// The <see cref="IrEffects"/> a direct interrupt service call has, from its
/// <see cref="InterruptServiceEntry"/>: the implementation receives its inputs as C# arguments
/// and stores nothing, so the call reads only its printed arguments (added by the caller, the
/// way it does for every call) - a bound core returns every contract output, so no output reads
/// its pre-call value. Only the result targets are must-written; may-write is what the service
/// writes, and the call touches memory
/// always and I/O ports when the service does. The call delivers no hardware interrupt: no
/// event delivery occurs inside the selected cores (delivery stays on the separate event-check
/// statement). The call builds the interrupt frame the interpreted <c>int</c> pushes and pops it
/// like <c>iret</c>, so it reads the frame's SS/SP/CS/FLAGS/DF in addition to the service inputs.
/// </summary>
internal static class ServiceContractEffects {
    /// <summary>The effects of <paramref name="node"/>, without the reads of its printed
    /// arguments (the caller adds them, the way it does for every call).</summary>
    public static IrEffects Of(InterruptServiceCallNode node) {
        CpuValues resultMask = node.Results.Aggregate(CpuValues.None,
            (mask, result) => mask | IrEffectsVisitor.Of(result, false).Reads);
        // Argument reads come from the call's children, not the contract. With a frame the call
        // also reads SS/SP to push and pop and CS/FLAGS (the pushed image); the popped FLAGS keeps
        // its pre-call value except where the service wrote a declared flag result into the frame.
        // Without a frame only the entry hook's stack-pointer read remains, and only while the
        // call runs it.
        CpuValues reads = CpuValues.None;
        if (node.WritesFrame) {
            reads = CpuValues.Ss | CpuValues.Sp | CpuValues.Cs
                | CpuValues.StatusFlags | CpuValues.Direction;
        } else if (node.RunsEntryHook) {
            reads = CpuValues.Ss | CpuValues.Sp;
        }
        if (node.Contract.Transfer is ServiceTransferKind.RunsChild) {
            // An EXEC call loads and runs a whole child program: it can write anything, but it
            // reads only the contract's inputs and the frame at the site itself - the observed
            // child entries' own inputs are charged through the call-site targets. Only the
            // result targets are must-written by the call itself.
            return new IrEffects(reads | node.Contract.Registers.Reads, resultMask, CpuValues.All,
                IrTokens.All, IrTokens.All, false, false);
        }
        CpuValues mayWrite = node.Contract.Registers.Writes;
        IrTokens tokens = node.Contract.Io ? IrTokens.All : IrTokens.Memory;
        return new IrEffects(reads, resultMask, mayWrite, tokens, tokens, false, false);
    }

    /// <summary>The effects of a generic <c>InterruptCall</c> that still dispatches the installed
    /// handler (<see cref="InterruptSiteKind.BuiltInDispatch"/>): the real dispatch builds the
    /// interrupt frame and runs the dispatcher, so the call reads the contract's inputs plus the
    /// frame's SS/SP/CS/FLAGS/DF, must-writes nothing (the iret restores the frame) and may-write
    /// the contract's outputs. Anything else is unknown.</summary>
    public static IrEffects Of(InterruptCallHelperNode node) {
        if (node.Kind != InterruptSiteKind.BuiltInDispatch
            || node.Service is not { Services: { } services }) {
            return IrEffects.Unknown;
        }
        InterruptServiceEntry? entry = services switch {
            HandlerServices.SingleService single => single.Service,
            HandlerServices.SelectedServices selected when node.SelectorValue is { } selectorValue =>
                selected.Services.GetValueOrDefault(selectorValue),
            _ => null
        };
        if (entry is null || ReferenceEquals(entry.Contract, ServiceContract.Unknown)) {
            return IrEffects.Unknown;
        }
        IrEffects effects = Of(entry.Contract);
        CpuValues reads = effects.Reads | CpuValues.Ss | CpuValues.Sp
            | CpuValues.Cs | CpuValues.StatusFlags | CpuValues.Direction;
        // The int writes the FLAGS/CS/IP frame and its iret reads it back, whatever the service
        // itself does to memory.
        return new IrEffects(reads, effects.MustWrite, effects.MayWrite,
            effects.TokenReads | IrTokens.Memory, effects.TokenWrites | IrTokens.Memory,
            effects.MayFault, effects.DeliversInterrupts);
    }

    /// <summary>The effects a service performs per its <paramref name="contract"/> alone, without
    /// any frame or dispatch reads: the contract's register inputs are read, nothing is
    /// must-written and the contract's outputs may be written; the memory token is read, written
    /// or both per the contract's memory effect, and I/O adds the I/O token both ways. The call
    /// cannot fault and delivers no hardware interrupt.</summary>
    public static IrEffects Of(ServiceContract contract) {
        IrTokens tokenReads = IrTokens.None;
        IrTokens tokenWrites = IrTokens.None;
        if (contract.Memory is ServiceMemoryEffect.Read or ServiceMemoryEffect.ReadWrite) {
            tokenReads |= IrTokens.Memory;
        }
        if (contract.Memory is ServiceMemoryEffect.Write or ServiceMemoryEffect.ReadWrite) {
            tokenWrites |= IrTokens.Memory;
        }
        if (contract.Io) {
            tokenReads |= IrTokens.Io;
            tokenWrites |= IrTokens.Io;
        }
        return new IrEffects(contract.Registers.Reads, CpuValues.None,
            contract.Registers.Writes, tokenReads, tokenWrites, false, false);
    }

    /// <summary>Whether <paramref name="node"/> or one of its descendants is an interrupt site
    /// whose effects the contract fully describes: a typed
    /// <see cref="InterruptServiceCallNode"/>, or a <see cref="InterruptCallHelperNode"/> whose
    /// <see cref="Of(InterruptCallHelperNode)"/> is not <see cref="IrEffects.Unknown"/>. Such a
    /// site is charged by its own statement effects, not by the dispatcher stub's convention.</summary>
    public static bool DescribesSite(IVisitableAstNode node) =>
        HasKnownEffects(node) || IrAstChildren.Descendants(node).Any(HasKnownEffects);

    /// <summary>Whether <paramref name="node"/> itself (not a descendant) is a known-effect
    /// interrupt site.</summary>
    internal static bool HasKnownEffects(IVisitableAstNode node) =>
        node is InterruptServiceCallNode
            || (node is InterruptCallHelperNode helper && Of(helper) != IrEffects.Unknown);
}
