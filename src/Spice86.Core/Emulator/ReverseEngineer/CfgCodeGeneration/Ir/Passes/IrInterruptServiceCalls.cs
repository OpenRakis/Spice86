namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Dos;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;

using System.Linq;
using System.Reflection;

/// <summary>
/// Turns an <c>int</c> site into a direct call of the service's declared implementation
/// (<c>Disk.ReadSectors(dl, al, es, bx, cx, dx)</c>) when the whole program lets it: the site is
/// <see cref="InterruptSiteKind.BuiltInNotAnalysed"/>, every <c>int</c> site on that vector
/// observed only the installed stub
/// (<see cref="HardwareInterruptEffectBuilder.HasOnlyBuiltInTargets"/>), the selector register
/// is a known constant whose entry has a precise contract and an implementation binding, and the
/// handler is reachable through exactly one <see cref="CSharpOverrideHelper"/> property. The
/// call takes the contract's effects, so values stay live across it and only the writes the
/// service makes die. Every other site keeps the untyped <c>InterruptCall</c> with the reason as
/// its kind.
/// Runs right after <see cref="IrConstants"/>: it reads the constant facts over the first SSA
/// build and keeps that SSA consistent by replacing every changed statement site.
/// <para>Legality, in the effects model (<c>CpuValues</c>, <c>IrTokens</c>, <c>MayFault</c>,
/// <c>DeliversInterrupts</c>; <c>IrSsa.SameVersions</c> for the moves it implies):</para>
/// <list type="bullet">
/// <item>IS-1: the site is typed only when every <c>int</c> site of the program on the vector
/// observed only the installed stub; a program handler, a hook, an uninstalled-then-seen vector
/// or a site with no observed target anywhere on the vector keeps the untyped call as
/// <see cref="InterruptSiteKind.Hooked"/> (the direct call could not deliver the observed
/// behaviour, and a vector check cannot repair a handler the program replaced).</item>
/// <item>IS-2: the selector register's bytes must be known (not undetermined) at the site;
/// otherwise the call keeps <c>InterruptCall</c> as <see cref="InterruptSiteKind.SelectorUnknown"/>.</item>
/// <item>IS-3: the selector value must have a precise contract bound to an implementation whose
/// restrictions the constant inputs satisfy; an <c>Unknown</c> contract or a missing entry keeps
/// <c>InterruptCall</c> as <see cref="InterruptSiteKind.NoContract"/>, while a service without an
/// implementation (or an unsatisfied binding restriction) keeps real dispatch as
/// <see cref="InterruptSiteKind.BuiltInDispatch"/> with the precise contract's normal-return
/// effects.</item>
/// <item>IS-4: the typed call's effects are the contract's: reads are only the explicit
/// input arguments (a conditionally-preserved output arrives as an explicit input binding
/// the pre-call value); only the result targets are must-written;
/// may-write is <c>Writes</c>; tokens are Memory always and Io when the service does I/O;
/// <c>DeliversInterrupts</c> is not set: no event delivery occurs inside the selected cores,
/// delivery after the call is the separate event-check statement.</item>
/// </list>
/// </summary>
internal static class IrInterruptServiceCalls {
    /// <summary>The public non-indexed <see cref="CSharpOverrideHelper"/> properties whose type
    /// is an interrupt handler: the receivers generated service calls print through.</summary>
    private static readonly Lazy<IReadOnlyList<PropertyInfo>> ReceiverProperties = new(() =>
        [.. typeof(CSharpOverrideHelper).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0
                && property.GetMethod is { IsPublic: true }
                && typeof(IInterruptHandler).IsAssignableFrom(property.PropertyType))]);

    /// <summary>Replaces every analysable <c>int</c> statement of <paramref name="method"/> by its
    /// typed service call or by the same call with its refined kind; returns the number of
    /// statements that became typed calls. The SSA sites of the changed statements are replaced
    /// too: a typed call's effects are narrower than the untyped one's, so verification never sees
    /// a stale site even when no statement became typed.</summary>
    /// <param name="hasOnlyBuiltInTargets">Whether every <c>int</c> site of the program on a
    /// vector observed only the installed stub.</param>
    public static int Apply(IrMethod method, IrSsa ssa, IrConstantFacts facts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets) {
        int changed = 0;
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (statement.Node is not InterruptCallHelperNode {
                        Kind: InterruptSiteKind.BuiltInNotAnalysed, Service: { } service
                    } call) {
                    continue;
                }
                IVisitableAstNode replacement = Classify(call, service, block, index, facts,
                    hasOnlyBuiltInTargets);
                if (ReferenceEquals(replacement, call)) {
                    continue;
                }
                IrStatement typedStatement = IrStatement.Create(replacement, statement.Origin);
                block.Statements[index] = typedStatement;
                if (ssa.HasSite(statement)) {
                    ssa.Replace(statement, typedStatement);
                }
                changed++;
            }
        }
        return changed;
    }

    private static IVisitableAstNode Classify(InterruptCallHelperNode call,
        InterruptServiceVector service, IrBlock block, int index, IrConstantFacts facts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets) {
        InterruptServiceEntry? entry;
        ServiceSelector? selector = null;
        int? selectorValue = null;
        if (service.Services is HandlerServices.SingleService single) {
            entry = single.Service;
        } else {
            HandlerServices.SelectedServices selected =
                (HandlerServices.SelectedServices)service.Services;
            if (!TrySelectorValue(selected.Selector, block, index, facts, out int value)) {
                return new InterruptCallHelperNode(InterruptSiteKind.SelectorUnknown, service, null,
                    [.. call.Arguments]);
            }
            selector = selected.Selector;
            selectorValue = value;
            entry = selected.Services.GetValueOrDefault(value);
        }
        if (!hasOnlyBuiltInTargets(service)) {
            return new InterruptCallHelperNode(InterruptSiteKind.Hooked, service, selectorValue,
                [.. call.Arguments]);
        }
        if (entry is null || ReferenceEquals(entry.Contract, ServiceContract.Unknown)) {
            return new InterruptCallHelperNode(InterruptSiteKind.NoContract, service,
                selectorValue, [.. call.Arguments]);
        }
        if (entry.Implementation is null
            || !BindingSupported(entry, block, index, facts)
            || ReceiverName(service.HandlerType) is not { } receiverName) {
            return new InterruptCallHelperNode(InterruptSiteKind.BuiltInDispatch, service,
                selectorValue, [.. call.Arguments]);
        }
        return new InterruptServiceCallNode(service.VectorNumber, selector, entry,
            receiverName, [.. entry.Inputs.Select(input => InputNodeOf(entry, input))]) {
            Results = [.. ServiceCallSignature.Outputs(entry.Contract)
                .Select(output => IrValueSets.NodeOf(output.Mask))]
        };
    }

    /// <summary>The bound input value of <paramref name="input"/>: the <c>DS:DX</c> binding of a
    /// service that takes a string is the segmented address it reads, every other binding is the
    /// register or flag node.</summary>
    private static IVisitableAstNode InputNodeOf(InterruptServiceEntry entry, CpuValues input) {
        if (input == (CpuValues.Ds | CpuValues.Dx)
            && entry.StringArgument != ServiceStringArgument.None) {
            return new SegmentedAddressNode(IrValueSets.NodeOf(CpuValues.Ds), IrValueSets.NodeOf(CpuValues.Dx));
        }
        return IrValueSets.NodeOf(input);
    }

    /// <summary>Whether the site's constant selector inputs satisfy the restrictions the bound
    /// implementation carries: a file-attribute call dispatches only a proven supported
    /// operation, a memory-allocation call only a proven subfunction (the dispatcher throws for
    /// the others), every other entry is unrestricted.</summary>
    private static bool BindingSupported(InterruptServiceEntry entry, IrBlock block, int index,
        IrConstantFacts facts) {
        if (entry.Implementation?.DeclaringType != typeof(DosInt21Handler)) {
            return true;
        }
        string name = entry.Implementation.Name;
        if (name is not nameof(DosInt21Handler.GetSetFileAttributes)
            and not nameof(DosInt21Handler.MemoryAllocationStrategy)) {
            return true;
        }
        RegisterNode al = (RegisterNode)IrValueSets.NodeOf(CpuValues.Al);
        if (!facts.TryGetBefore(block, index, al, out ulong operation)) {
            return false;
        }
        if (name == nameof(DosInt21Handler.GetSetFileAttributes)) {
            return DosInt21Handler.IsFileAttributeOperationSupported((byte)operation);
        }
        return operation <= (byte)DosInt21Handler.AllocationStrategySubFunction
            .SetUpperMemoryBlockState;
    }

    /// <summary>The name of the unique <see cref="CSharpOverrideHelper"/> property holding the
    /// installed handler of <paramref name="handlerType"/>, or <see langword="null"/> when the
    /// handler has none.</summary>
    internal static string? ReceiverName(Type handlerType) {
        List<PropertyInfo> matches = [.. ReceiverProperties.Value
            .Where(property => property.PropertyType == handlerType)];
        if (matches.Count > 1) {
            throw new InvalidOperationException(
                $"CSharpOverrideHelper has {matches.Count} properties of type {handlerType.Name}");
        }
        return matches.Count == 0 ? null : matches[0].Name;
    }

    private static bool TrySelectorValue(ServiceSelector selector, IrBlock block, int index,
        IrConstantFacts facts, out int value) {
        value = 0;
        RegisterNode register = (RegisterNode)IrValueSets.NodeOf(selector.ToCpuValues());
        if (!facts.TryGetBefore(block, index, register, out ulong registerValue)) {
            return false;
        }
        value = (int)registerValue;
        return true;
    }
}
