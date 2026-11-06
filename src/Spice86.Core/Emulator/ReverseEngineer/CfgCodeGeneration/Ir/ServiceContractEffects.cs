namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
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
/// statement), and no interrupt frame is built, so the frame's SS/SP/FLAGS reads are gone too.
/// </summary>
internal static class ServiceContractEffects {
    /// <summary>The effects of <paramref name="node"/>, without the reads of its printed
    /// arguments (the caller adds them, the way it does for every call).</summary>
    public static IrEffects Of(InterruptServiceCallNode node) {
        CpuValues resultMask = node.Results.Aggregate(CpuValues.None,
            (mask, result) => mask | IrEffectsVisitor.Of(result, false).Reads);
        // The printed RecordCallerStack bookkeeping of a DOS call reads the caller SS:SP.
        CpuValues reads = CpuValues.None;
        if (node.Vector == 0x21 && DosInt21Handler.SavesCallerStack((byte)node.SelectorValue)) {
            reads = CpuValues.Ss | CpuValues.Sp;
        }
        IrTokens tokens = node.Contract.Io ? IrTokens.All : IrTokens.Memory;
        return new IrEffects(reads, resultMask, node.Contract.Registers.Writes,
            tokens, tokens, false, false);
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
        CpuValues reads = entry.Contract.Registers.Reads | CpuValues.Ss | CpuValues.Sp
            | CpuValues.Cs | CpuValues.StatusFlags | CpuValues.Direction;
        IrTokens tokens = entry.Contract.Io ? IrTokens.All : IrTokens.Memory;
        return new IrEffects(reads, CpuValues.None, entry.Contract.Registers.Writes,
            tokens, tokens, false, false);
    }
}
