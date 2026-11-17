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
using Spice86.Shared.Emulator.Memory;

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
    /// <param name="programSetCurrentPsp">Whether the program set the current PSP itself through
    /// DOS AH=50h/55h; then every typed service call keeps writing the interrupt frame.</param>
    /// <param name="terminateResumedBatchLaunch">Whether a terminate during the run resumed by
    /// the batch engine launching the next program: that resume only exists as the interrupt
    /// stub's callback-moved-CS:IP path, so terminate calls keep the untyped call.</param>
    /// <param name="execChildEntries">The child program entries discovery observed the DOS
    /// dispatch callback move CS:IP to: the resolver list a typed EXEC call prints.</param>
    public static int Apply(IrMethod method, IrEditor editor, IrConstantFacts facts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets, bool programSetCurrentPsp,
        bool terminateResumedBatchLaunch,
        IReadOnlyList<SegmentedAddress> execChildEntries) {
        List<(IrBlock Block, int Index, IrStatement Replacement)> typed = [];
        foreach (IrBlock block in method.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (statement.Node is not InterruptCallHelperNode {
                        Kind: InterruptSiteKind.BuiltInNotAnalysed, Service: { } service
                    } call) {
                    continue;
                }
                IVisitableAstNode replacement = Classify(call, service, block, index, facts,
                    hasOnlyBuiltInTargets, programSetCurrentPsp, terminateResumedBatchLaunch,
                    execChildEntries);
                if (ReferenceEquals(replacement, call)) {
                    continue;
                }
                typed.Add((block, index, IrStatement.Create(replacement, statement.Origin)));
            }
        }
        foreach ((IrBlock block, int index, IrStatement replacement) in typed) {
            if (replacement.Node is InterruptServiceCallNode {
                    Contract.Transfer: ServiceTransferKind.Terminates
                } terminatingCall) {
                // A terminate never reaches the code after the call site: the statement becomes
                // the method's exit and the unobserved-return FailAsUntested it replaces is gone.
                editor.Remove(block, index);
                editor.SetTerminator(block,
                    new IrExit(block.Terminator.Origin, new ReturnNode(terminatingCall)));
                continue;
            }
            editor.Replace(block, index, replacement);
        }
        return typed.Count;
    }

    private static IVisitableAstNode Classify(InterruptCallHelperNode call,
        InterruptServiceVector service, IrBlock block, int index, IrConstantFacts facts,
        Func<InterruptServiceVector, bool> hasOnlyBuiltInTargets, bool programSetCurrentPsp,
        bool terminateResumedBatchLaunch,
        IReadOnlyList<SegmentedAddress> execChildEntries) {
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
            if (entry is { Variants.Count: > 0 } baseEntry) {
                RegisterNode al = (RegisterNode)IrValueSets.NodeOf(CpuValues.Al);
                if (facts.TryGetBefore(block, index, al, out ulong alValue)) {
                    entry = baseEntry.Variants
                        .FirstOrDefault(variant => variant.Al == (int)alValue) ?? entry;
                }
            }
        }
        if (!hasOnlyBuiltInTargets(service)) {
            return new InterruptCallHelperNode(InterruptSiteKind.Hooked, service, selectorValue,
                [.. call.Arguments]);
        }
        if (entry is null || ReferenceEquals(entry.Contract, ServiceContract.Unknown)) {
            return new InterruptCallHelperNode(InterruptSiteKind.NoContract, service,
                selectorValue, [.. call.Arguments]);
        }
        if (entry.Contract.Transfer is ServiceTransferKind.Terminates
            && (terminateResumedBatchLaunch || programSetCurrentPsp
                || index != block.Statements.Count - 1
                || block.Terminator is not IrExit { Transfer: FailAsUntestedNode })) {
            // A terminate only prints as a method exit when nothing after the call was observed
            // and the resume is consumable by the override chain: a program whose terminate
            // relaunched a batch-kept program resumes through the stub's callback-moved-CS:IP
            // path only; a program that manages its own PSP (AH=50h/55h) can resume at the
            // arbitrary address the recorded caller stack names, which generated control flow
            // cannot express; and a site with observed code after it could fall through. All
            // keep the untyped call.
            return new InterruptCallHelperNode(InterruptSiteKind.BuiltInDispatch, service,
                selectorValue, [.. call.Arguments]);
        }
        // A child-running service prints ExecChild and never invokes an implementation.
        if ((entry.Implementation is null
                && entry.Contract.Transfer is not ServiceTransferKind.RunsChild)
            || !BindingSupported(entry, block, index, facts)
            || ReceiverName(service.HandlerType) is not { } receiverName) {
            return new InterruptCallHelperNode(InterruptSiteKind.BuiltInDispatch, service,
                selectorValue, [.. call.Arguments]);
        }
        bool runsChild = entry.Contract.Transfer is ServiceTransferKind.RunsChild;
        List<ValueNode> results = [.. ServiceCallSignature.Outputs(entry.Contract)
            .Select(output => IrValueSets.NodeOf(output.Mask))];
        if (runsChild) {
            // A returned child restores the parent's DS/ES to its own PSP: the call reports them
            // as outputs even though the contract only names AX and the carry flag.
            results.Add(IrValueSets.NodeOf(CpuValues.Ds));
            results.Add(IrValueSets.NodeOf(CpuValues.Es));
        }
        return new InterruptServiceCallNode(service.VectorNumber, selector, entry,
            receiverName, [.. entry.Inputs.Select(input => InputNodeOf(entry, input))]) {
            RunsEntryHook = RunsEntryHook(entry),
            WritesFrame = programSetCurrentPsp || entry.ReadsFrame || runsChild,
            Results = results,
            ChildEntries = runsChild ? execChildEntries : [],
            ReturnSegment = call.Arguments[0],
            ReturnOffset = call.Arguments[1]
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

    /// <summary>Whether the typed call of <paramref name="entry"/> runs the handler's entry
    /// bookkeeping: a DOS service saves the caller stack exactly when
    /// <see cref="DosInt21Handler.SavesCallerStack"/> accepts its function number, every other
    /// handler's hook is the base no-op and always runs.</summary>
    private static bool RunsEntryHook(InterruptServiceEntry entry) {
        if (entry.Implementation?.DeclaringType != typeof(DosInt21Handler)) {
            return true;
        }
        return DosInt21Handler.SavesCallerStack((byte)entry.SelectorValue);
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
