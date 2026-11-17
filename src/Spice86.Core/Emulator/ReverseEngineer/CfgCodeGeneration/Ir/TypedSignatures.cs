namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// The per-program plan of typed signatures: which single-entry returning methods print
/// <c>public virtual ushort f(ushort ax, ushort bx)</c> plus an <c>f_Asm</c> entry wrapper, and
/// which call sites print the typed call helper form. Computed once after
/// <see cref="IrCallingConventions"/>; the IR and its effects do not change. The emitted body
/// assigns each parameter to its register before anything else, so callers that are not
/// generated code (hand-written C#, a subclass) enter with any argument values.
/// <para>A second pass over the typed set then drops the parameters and outputs that only pass
/// through an untyped call: a register or flag the body merely feeds to such a call stays a
/// machine input, and an output only such a call defines stays machine state the caller reads
/// back. The convention sets do not change, so the call summary still reads every input and
/// writes every change.</para>
/// </summary>
internal sealed class TypedSignatures {
    private readonly Dictionary<string, TypedSignature> _byMethodName;

    private TypedSignatures(Dictionary<string, TypedSignature> byMethodName) {
        _byMethodName = byMethodName;
    }

    /// <summary>The signature of <paramref name="methodName"/>, or <c>null</c> when the method stays
    /// untyped.</summary>
    public TypedSignature? Of(string methodName) =>
        _byMethodName.TryGetValue(methodName, out TypedSignature? signature) ? signature : null;

    /// <summary>The method every delegate reference uses: <paramref name="methodName"/> suffixed
    /// with <c>_Asm</c> for a typed method (its entry wrapper), unchanged otherwise.</summary>
    public string DelegateNameOf(string methodName) =>
        _byMethodName.ContainsKey(methodName) ? methodName + "_Asm" : methodName;

    /// <summary>The signature a call statement may use: <paramref name="node"/> is a call whose
    /// only observed target is a typed method's entry of the same operand width, with no
    /// redirected return, no observed mid-call landings and an observed continuation;
    /// <c>null</c> for any other call site (it keeps the untyped helper).</summary>
    public TypedSignature? TypedCallOf(ProcedureCallHelperNode node) {
        if (node.SingleObservedTarget && node.ObservedContinuation && !node.ObservedLandings
            && node.Arguments[^1] is MethodGroupNode { SingleEntry: true, LoadOffset: 0 } group
            && _byMethodName.TryGetValue(group.MethodName, out TypedSignature? signature)
            && signature.Width == node.Width) {
            return signature;
        }
        return null;
    }

    /// <summary>Whether <paramref name="node"/> prints the typed call form
    /// (<see cref="TypedCallOf"/> is not <c>null</c>).</summary>
    public bool IsTypedCall(ProcedureCallHelperNode node) => TypedCallOf(node) is not null;

    /// <summary>Computes the typed signature of every eligible method of <paramref name="methods"/>:
    /// one entry that returns, every exit a <c>ret</c> / <c>retf</c> of the same distance and popped
    /// byte count or an abort, no <c>int</c> or callback, and at least one input or output.</summary>
    public static TypedSignatures Compute(IReadOnlyList<PreparedMethod> methods,
        IrCallingConventions conventions) {
        Dictionary<string, TypedSignature> byName = methods
            .Select(source => (source.Method.MethodName, Signature: SignatureOf(source, conventions)))
            .Where(pair => pair.Signature is not null)
            .ToDictionary(pair => pair.MethodName,
                pair => pair.Signature ?? throw new InvalidOperationException("SignatureOf gave no signature."));
        TypedSignatures signatures = new(byName);
        signatures.RefineSignatures(methods, conventions);
        if (signatures.ResolveTailCalls()) {
            signatures.RefineSignatures(methods, conventions);
        }
        signatures.AssignNames(methods);
        return signatures;
    }

    /// <summary>Confirms or drops every provisional signature <see cref="ExitsAreTyped"/> admitted
    /// with a tail-call exit: a target must itself be typed with the same outputs in the same order
    /// (no projection) and the same distance and popped bytes, so <c>return f(args);</c> hands the
    /// caller's callers exactly the tuple the signature promises; any other target makes the method
    /// stay untyped. Iterates so confirmed targets can admit their own callers in turn; a signature
    /// whose targets never resolve (a cycle or a dropped target) is dropped. Returns whether any
    /// signature was dropped - callers then re-run <see cref="RefineSignatures"/> because a dropped
    /// target turns calls back to untyped.</summary>
    private bool ResolveTailCalls() {
        bool dropped = false;
        while (true) {
            bool progress = false;
            foreach ((string name, TypedSignature signature) in _byMethodName.ToList()) {
                if (signature.TailCallTargets is not { } targets) {
                    continue;
                }
                bool allConfirmed = true;
                bool failed = false;
                foreach (string target in targets) {
                    if (!_byMethodName.TryGetValue(target, out TypedSignature? targetSignature)
                            || !SameOutputsAndReturn(signature, targetSignature)) {
                        failed = true;
                        break;
                    }
                    if (targetSignature.TailCallTargets is not null) {
                        allConfirmed = false;
                    }
                }
                if (failed) {
                    _byMethodName.Remove(name);
                    dropped = progress = true;
                } else if (allConfirmed) {
                    signature.TailCallTargets = null;
                    progress = true;
                }
            }
            if (!progress) {
                break;
            }
        }
        foreach ((string name, TypedSignature signature) in _byMethodName.ToList()) {
            if (signature.TailCallTargets is not null) {
                _byMethodName.Remove(name);
                dropped = true;
            }
        }
        return dropped;
    }

    private static bool SameOutputsAndReturn(TypedSignature caller, TypedSignature callee) =>
        caller.Outputs.Count == callee.Outputs.Count
            && caller.Outputs.Zip(callee.Outputs).All(pair => pair.First.Mask == pair.Second.Mask)
            && caller.Distance == callee.Distance
            && caller.Width == callee.Width
            && caller.PoppedBytes == callee.PoppedBytes;

    /// <summary>Second pass over the typed methods, with the pass-1 set known: an input the body
    /// only feeds to an untyped call, and an output only an untyped call writes, stay machine
    /// state instead of becoming a parameter or a tuple element. The convention sets do not
    /// change: the call summary still reads every input and writes every change.</summary>
    private void RefineSignatures(IReadOnlyList<PreparedMethod> methods,
        IrCallingConventions conventions) {
        foreach (PreparedMethod source in methods) {
            if (!_byMethodName.TryGetValue(source.Method.MethodName,
                    out TypedSignature? signature)) {
                continue;
            }
            CfgCodePartitionEntry entry = source.Method.Entries[0];
            IrEntry irEntry = source.IrMethod.Entries
                .Single(methodEntry => methodEntry.Node.Equals(entry.Node));
            CpuValues inputs = conventions.EffectOf(entry).Reads;
            CpuValues demanded = conventions.OutputsOf(entry);
            CpuValues outputs = demanded & ~MachineOutputs(source, demanded);
            Dictionary<string, int> nameCounters = new(StringComparer.Ordinal);
            _byMethodName[source.Method.MethodName] = new TypedSignature {
                Parameters = GenericNames(CpuValueParameters.Decompose(
                    inputs & ~MachineInputs(source, irEntry, inputs, IsTypedCall)),
                    nameCounters),
                Outputs = GenericNames(CpuValueParameters.Decompose(outputs), nameCounters),
                Distance = signature.Distance,
                Width = signature.Width,
                PoppedBytes = signature.PoppedBytes,
                EntryBlock = signature.EntryBlock,
                TailCallTargets = signature.TailCallTargets
            };
        }
    }

    /// <summary>The G4.2 names of every typed signature, iterated to a fixed point because a
    /// callee parameter's name is a sink a caller's name is read from (the spec's backwards
    /// propagation). Each round recomputes every name from the current names, so convergence is
    /// monotonic over a finite name set; a round cap of 8 turns an oscillation into a bug's
    /// failure instead of a hang.</summary>
    private void AssignNames(IReadOnlyList<PreparedMethod> methods) {
        for (int round = 0; ; round++) {
            bool changed = false;
            foreach (PreparedMethod prepared in methods) {
                if (_byMethodName.TryGetValue(prepared.Method.MethodName, out TypedSignature? signature)) {
                    changed |= AssignNames(prepared, signature);
                }
            }
            if (!changed) {
                return;
            }
            if (round >= 7) {
                throw new InvalidOperationException("Typed signature naming did not converge.");
            }
        }
    }

    private bool AssignNames(PreparedMethod prepared, TypedSignature signature) {
        HashSet<string> taken = IrMethodEmitter.ReservedLocalNames(prepared.IrMethod, null,
            prepared.FramePlan, null);
        bool changed = false;
        List<TypedParameter> parameters = (List<TypedParameter>)signature.Parameters;
        for (int index = 0; index < parameters.Count; index++) {
            string? hint = ValueOf(parameters[index].Mask) is { } value
                ? ParameterHint(prepared, signature, parameters[index], value)
                : null;
            string name = hint is not null && RoleNames.IsIdentifier(hint)
                ? RoleNames.Next(hint, taken)
                : GenericValueNames.Next(GenericValueNames.Prefix(parameters[index].Type), taken);
            taken.Add(name);
            if (parameters[index].Name != name) {
                parameters[index] = parameters[index] with { Name = name };
                changed = true;
            }
        }
        List<TypedParameter> outputs = (List<TypedParameter>)signature.Outputs;
        for (int index = 0; index < outputs.Count; index++) {
            string name = RoleNames.Next("result", taken);
            taken.Add(name);
            if (outputs[index].Name != name) {
                outputs[index] = outputs[index] with { Name = name };
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>The semantic name of one typed parameter: the single sink name when the
    /// entry-version value is passed to exactly one kind of named contract or callee parameter,
    /// else a role name, or <see langword="null"/> for the G4.1 generic name.</summary>
    private string? ParameterHint(PreparedMethod prepared, TypedSignature signature,
        TypedParameter parameter, IrSsaValue value) {
        int entryVersion = prepared.Ssa.VersionBefore(signature.EntryBlock, 0, value);
        List<IVisitableAstNode> roots = [];
        List<CfgInstruction> origins = [];
        foreach (IrBlock block in prepared.IrMethod.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (prepared.Ssa.VersionUsed(statement, value) != entryVersion) {
                    continue;
                }
                roots.Add(statement.Node);
                if (statement.Origin is CfgInstruction instruction) {
                    origins.Add(instruction);
                }
            }
        }
        HashSet<string> sinks = RoleNames.SinkNames(roots, value, null, null, this);
        if (RoleNames.UniqueName(sinks) is { } sink) {
            return sink;
        }
        bool isSegment = (parameter.Mask
                & (CpuValues.Es | CpuValues.Cs | CpuValues.Ss | CpuValues.Ds
                    | CpuValues.Fs | CpuValues.Gs)) != CpuValues.None;
        return RoleNames.RoleName(roots, value, null, isSegment, origins);
    }

    /// <summary>Whether <paramref name="statement"/> contains a call the typed-call check refuses:
    /// an untyped callee reads live machine state, so a version that only feeds such a call stays a
    /// machine value.</summary>
    private static bool HasUntypedCall(IrStatement statement,
        Func<ProcedureCallHelperNode, bool> isTypedCall) =>
        IrAstChildren.Descendants(statement.Node).Any(node =>
            node is ProcedureCallHelperNode call && !isTypedCall(call)
            || IsMachineInterrupt(node));

    /// <summary>A frame-writing typed service call or a built-in-dispatch <c>int</c>: both read
    /// the FLAGS image (and, for a dispatch, the registers the handler may read) from the machine
    /// like an untyped call does, so the bytes they read stay machine inputs.</summary>
    private static bool IsMachineInterrupt(IVisitableAstNode node) =>
        node is InterruptServiceCallNode { WritesFrame: true }
            or InterruptCallHelperNode { Kind: InterruptSiteKind.BuiltInDispatch };

    /// <summary>The input bytes whose value the method only saves, merges, or feeds an untyped
    /// call: a general or segment register is a machine input when the worklist over its entry
    /// version's uses meets only save pushes (<see cref="IrPreservation.IsPushOf"/>), event
    /// checks, untyped-call reads (<see cref="HasUntypedCall"/>), phis and partial definitions
    /// that do not read its bytes - the bytes a pop restore writes end the carried value. A flag
    /// qualifies the same way except that only untyped-call reads, event checks and phis are
    /// ignored: a flag save is a real read. A machine input stays in the convention's inputs, so
    /// the typed call reads it from the machine and the body reads the machine register wherever
    /// it reads that version; only the parameter and its copy-in line go. A terminator, exit or
    /// other statement read disqualifies.</summary>
    private static CpuValues MachineInputs(PreparedMethod source, IrEntry irEntry, CpuValues inputs,
        Func<ProcedureCallHelperNode, bool> isTypedCall) {
        CpuValues machine = CpuValues.None;
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()) {
            bool register = value <= IrSsaValue.Gs;
            CpuValues bytes0 = IrSsaValues.MaskOf(value) & inputs;
            if (bytes0 == CpuValues.None) {
                continue;
            }
            Queue<(int Version, CpuValues Bytes)> worklist = new();
            worklist.Enqueue((source.Ssa.MethodEntryVersion(irEntry.Block, value), bytes0));
            HashSet<int> visited = [];
            bool realRead = false;
            while (worklist.Count > 0 && !realRead) {
                (int version, CpuValues bytes) = worklist.Dequeue();
                if (!visited.Add(version)) {
                    continue;
                }
                foreach (IrSsaUse use in source.Ssa.Uses(version)) {
                    if (use.Phi is { } phi) {
                        worklist.Enqueue((phi.Result, bytes));
                        continue;
                    }
                    if (use.Statement is not { } statement) {
                        if (use.Terminator is IrExit exit
                                && IrAstChildren.Descendants(exit.Transfer)
                                    .Any(node => node.GetType() == typeof(MethodCallNode))) {
                            continue;
                        }
                        realRead = true;
                        break;
                    }
                    if (statement.Node is CheckExternalEventsNode
                        || HasUntypedCall(statement, isTypedCall)) {
                        continue;
                    }
                    if (register && IrPreservation.IsPushOf(statement, value, out _)) {
                        continue;
                    }
                    if (register && use.ConditionalNewVersion >= 0
                        && (statement.Effects.Reads & bytes) == CpuValues.None) {
                        CpuValues carried = bytes & ~statement.Effects.MustWrite;
                        if (carried != CpuValues.None) {
                            worklist.Enqueue((use.ConditionalNewVersion, carried));
                        }
                        continue;
                    }
                    realRead = true;
                    break;
                }
            }
            if (!realRead) {
                machine |= bytes0;
            }
        }
        return machine;
    }

    /// <summary>The output bytes whose version at every return exit is defined by an untyped call
    /// (or by a phi whose operands all are): the caller reads them back from the machine register,
    /// which the typed body writes anyway, so no tuple element is needed.</summary>
    private CpuValues MachineOutputs(PreparedMethod source, CpuValues outputs) {
        List<IrBlock> returnExits = [.. source.IrMethod.Blocks.Where(block =>
            block.Terminator is IrExit exit
            && IrAstChildren.Descendants(exit.Transfer).OfType<ProcedureReturnHelperNode>().Any())];
        CpuValues machine = CpuValues.None;
        if (returnExits.Count == 0) {
            return machine;
        }
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()) {
            CpuValues bytes = IrSsaValues.MaskOf(value) & outputs;
            if (bytes == CpuValues.None) {
                continue;
            }
            if (returnExits.All(block => DefinedByUntypedCall(source,
                    source.Ssa.VersionBefore(block, block.Statements.Count, value)))) {
                machine |= bytes;
            }
        }
        return machine;
    }

    private bool DefinedByUntypedCall(PreparedMethod source, int version) {
        HashSet<int> visited = [];
        Queue<int> worklist = new([version]);
        while (worklist.Count > 0) {
            int current = worklist.Dequeue();
            if (!visited.Add(current)) {
                continue;
            }
            if (source.Ssa.PhiDefining(current) is { } phi) {
                foreach ((_, int operand) in phi.Operands) {
                    worklist.Enqueue(operand);
                }
                continue;
            }
            if (source.Ssa.DefiningStatement(current) is not { } statement
                || !HasUntypedCall(statement, IsTypedCall)) {
                return false;
            }
        }
        return true;
    }

    private static TypedSignature? SignatureOf(PreparedMethod source,
        IrCallingConventions conventions) {
        MethodPlan plan = source.Method;
        if (plan.Entries.Count != 1) {
            return null;
        }
        CfgCodePartitionEntry entry = plan.Entries[0];
        if (!conventions.Returns(entry)) {
            return null;
        }
        IrMethod method = source.IrMethod;
        if (!ExitsAreTyped(method, out CallDistance distance, out BitWidth width, out int poppedBytes,
                out bool hasReturns, out List<string>? tailTargets)) {
            return null;
        }
        if (HasInterruptOrCallback(method)) {
            return null;
        }
        CpuValues inputs = conventions.EffectOf(entry).Reads;
        CpuValues outputs = conventions.OutputsOf(entry);
        if ((inputs | outputs) == CpuValues.None) {
            return null;
        }
        IrEntry irEntry = method.Entries
            .Single(methodEntry => methodEntry.Node.Equals(entry.Node));
        IrCallingConventions.CalleeReturnEffect? proved =
            conventions.ReturnsToCallerOf(plan.MethodName);
        if (proved is null && tailTargets is { Count: > 0 }) {
            proved = BorrowedTailEffect(conventions, tailTargets);
            if (proved is not null) {
                if (hasReturns && (proved.Distance != distance || proved.Width != width
                        || proved.PoppedBytes != poppedBytes)) {
                    proved = null;
                } else {
                    distance = proved.Distance;
                    width = proved.Width;
                    poppedBytes = proved.PoppedBytes;
                }
            }
        }
        if (proved is null || proved.Distance != distance || proved.Width != width
                || proved.PoppedBytes != poppedBytes) {
            return null;
        }
        Dictionary<string, int> nameCounters = new(StringComparer.Ordinal);
        return new TypedSignature {
            Parameters = GenericNames(
                CpuValueParameters.Decompose(inputs & ~MachineInputs(source, irEntry, inputs,
                    _ => true)),
                nameCounters),
            Outputs = GenericNames(CpuValueParameters.Decompose(outputs), nameCounters),
            Distance = distance,
            Width = width,
            PoppedBytes = poppedBytes,
            EntryBlock = irEntry.Block,
            TailCallTargets = tailTargets
        };
    }

    /// <summary>The G4.1 generic parameter names: the width prefix plus a per-prefix number in
    /// describe order, one numbering for the signature's parameters and outputs.</summary>
    private static List<TypedParameter> GenericNames(List<TypedParameter> parameters,
        Dictionary<string, int> counters) {
        List<TypedParameter> named = new(parameters.Count);
        foreach (TypedParameter parameter in parameters) {
            string prefix = GenericValueNames.Prefix(parameter.Type);
            counters.TryGetValue(prefix, out int number);
            counters[prefix] = ++number;
            named.Add(parameter with { Name = $"{prefix}{number}" });
        }
        return named;
    }

    /// <summary>Every exit of <paramref name="method"/> is a <c>ret</c> / <c>retf</c> of the
    /// same distance, operand width and popped byte count, an abort (<c>FailAsUntested</c>, throw)
    /// or a tail call (a bare <see cref="MethodCallNode"/>, recorded in <paramref name="tailTargets"/>
    /// for the provisional signature <see cref="ResolveTailCalls"/> later confirms); at least one is
    /// a return or a tail call.</summary>
    private static IrCallingConventions.CalleeReturnEffect? BorrowedTailEffect(
        IrCallingConventions conventions, List<string> tailTargets) {
        IrCallingConventions.CalleeReturnEffect? effect = null;
        foreach (string target in tailTargets) {
            IrCallingConventions.CalleeReturnEffect? targetEffect =
                conventions.ReturnsToCallerOf(target);
            if (targetEffect is null
                || (effect is not null && (targetEffect.Distance != effect.Distance
                    || targetEffect.Width != effect.Width
                    || targetEffect.PoppedBytes != effect.PoppedBytes))) {
                return null;
            }
            effect = targetEffect;
        }
        return effect;
    }

    private static bool ExitsAreTyped(IrMethod method, out CallDistance distance, out BitWidth width,
        out int poppedBytes, out bool hasReturns, out List<string>? tailTargets) {
        distance = CallDistance.Near;
        width = BitWidth.WORD_16;
        poppedBytes = 0;
        hasReturns = false;
        tailTargets = null;
        int returns = 0;
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrExit exit) {
                continue;
            }
            List<IVisitableAstNode> nodes = [.. IrAstChildren.Descendants(exit.Transfer)];
            if (nodes.Any(node => node is InterruptReturnNode or CyclicJumpNode or CpuFaultTryCatchNode
                    or HltHelperNode)) {
                return false;
            }
            List<MethodCallNode> tailCalls = [.. nodes
                .Where(node => node.GetType() == typeof(MethodCallNode)).Cast<MethodCallNode>()];
            if (tailCalls.Count > 0) {
                (tailTargets ??= []).AddRange(tailCalls.Select(call => call.MethodName));
                continue;
            }
            List<ProcedureReturnHelperNode> returnHelpers = nodes.OfType<ProcedureReturnHelperNode>().ToList();
            if (returnHelpers.Count > 0) {
                foreach (ProcedureReturnHelperNode helper in returnHelpers) {
                    int popCount = PopCount(helper);
                    if (helper.Width is not (BitWidth.WORD_16 or BitWidth.DWORD_32) || popCount < 0
                        || (returns > 0 && (helper.Distance != distance || helper.Width != width
                            || popCount != poppedBytes))) {
                        return false;
                    }
                    distance = helper.Distance;
                    width = helper.Width;
                    poppedBytes = popCount;
                    returns++;
                }
                hasReturns = true;
                continue;
            }
            if (!nodes.Any(node => node is FailAsUntestedNode or ThrowNode or ReturnNode)) {
                return false;
            }
        }
        return returns > 0 || tailTargets is { Count: > 0 };
    }

    private static int PopCount(ProcedureReturnHelperNode helper) => helper.Arguments switch {
        [] => 0,
        [IVisitableAstNode argument] => PopCountArgument(argument),
        _ => -1
    };

    private static int PopCountArgument(IVisitableAstNode argument) {
        while (argument is ExplicitCastNode cast) {
            argument = cast.Value;
        }
        if (ConstantEvaluation.Resolved(argument) is not ConstantNode { Value: <= ushort.MaxValue } constant) {
            return -1;
        }
        return (int)constant.Value;
    }

    /// <summary>Whether any statement or terminator of <paramref name="method"/> is an
    /// <c>int</c> or a callback whose effects are not fully described by a service contract:
    /// either can end the program or move CS:IP, which the typed return check would turn into a
    /// failure. A typed <see cref="InterruptServiceCallNode"/> stays allowed - frame-writing or
    /// not - and so does a <see cref="InterruptSiteKind.BuiltInDispatch"/> site: both keep their
    /// own statement effects and read the FLAGS image from the machine, which
    /// <see cref="IsMachineInterrupt"/> accounts as a machine input.</summary>
    private static bool HasInterruptOrCallback(IrMethod method) =>
        method.Blocks.SelectMany(block =>
                block.Statements.Select(statement => statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator)))
            .Any(root => IrAstChildren.Descendants(root).Any(node =>
                node is ProgramInterruptCallHelperNode
                    or InterruptCallReturnedElsewhereHelperNode
                    or CallbackHelperNode or CallbackMovedCsIpHelperNode
                    || node is InterruptCallHelperNode { Kind: not InterruptSiteKind.BuiltInDispatch }));

    /// <summary>
    /// The read-position register / flag occurrences of <paramref name="method"/> that print as
    /// parameters: for each statement and each non-exit terminator root (a branch condition, a
    /// switch value), each <see cref="RegisterNode"/> / <see cref="SegmentRegisterNode"/>
    /// / single-flag <see cref="CpuFlagNode"/> occurrence whose used version is the entry version,
    /// keyed by node reference. An assignment target is never substituted: shared node objects can
    /// sit on both sides of <c>X = X op y</c>, so the write stays on the register. An ALU operand
    /// that reads the same register bytes as the target operand of the same call keeps the register
    /// too, so <c>X = op(X, X)</c> prints both operands alike.
    /// </summary>
    public IReadOnlyDictionary<IVisitableAstNode, string> BuildParameterReads(IrMethod method,
        IrSsa ssa, TypedSignature signature) {
        Dictionary<IrSsaValue, TypedParameter> parameterByValue = new();
        foreach ((IrSsaValue? Value, TypedParameter Parameter) pair in signature.Parameters
                .Select(parameter => (Value: ValueOf(parameter.Mask), Parameter: parameter))
                .Where(pair => pair.Value is not null)) {
            parameterByValue[pair.Value.GetValueOrDefault()] = pair.Parameter;
        }
        int[] entryVersions = new int[(int)IrSsaValue.Count];
        foreach (IrSsaValue value in parameterByValue.Keys) {
            entryVersions[(int)value] = ssa.MethodEntryVersion(signature.EntryBlock, value);
        }
        HashSet<IVisitableAstNode> targets = new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                CollectTargets(statement.Node, targets);
            }
            if (block.Terminator is IrExit exit) {
                CollectTargets(exit.Transfer, targets);
            } else {
                foreach (IVisitableAstNode root in IrAstChildren.RootsOf(block.Terminator)) {
                    CollectTargets(root, targets);
                }
            }
        }
        Dictionary<IVisitableAstNode, string> table = new(ReferenceEqualityComparer.Instance);
        int[] usedVersions = new int[(int)IrSsaValue.Count];
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                int[] used = UsedVersions(usedVersions, value => ssa.VersionUsed(statement, value));
                CollectReads(statement.Node, table, targets, parameterByValue, entryVersions, used);
            }
            if (block.Terminator is IrExit exit) {
                int[] used = UsedVersions(usedVersions, value => ssa.VersionUsed(exit, value));
                CollectReads(exit.Transfer, table, targets, parameterByValue, entryVersions, used);
            } else {
                int[] used = UsedVersions(usedVersions,
                    value => ssa.VersionUsed(block.Terminator, value));
                foreach (IVisitableAstNode root in IrAstChildren.RootsOf(block.Terminator)) {
                    CollectReads(root, table, targets, parameterByValue, entryVersions, used);
                }
            }
        }
        return table;
    }

    private static int[] UsedVersions(int[] buffer, Func<IrSsaValue, int> versionUsed) {
        Array.Fill(buffer, -1);
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()
                .Where(value => value != IrSsaValue.Count)) {
            buffer[(int)value] = versionUsed(value);
        }
        return buffer;
    }

    /// <summary>The assignment-target occurrences of <paramref name="root"/>: the register or flag
    /// node that is the left side of an assignment, a swap operand, a division result or a typed
    /// service call's result lvalue.</summary>
    private static void CollectTargets(IVisitableAstNode root, HashSet<IVisitableAstNode> targets) {
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            switch (node) {
                case BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment
                    when assignment.Left is RegisterNode or SegmentRegisterNode or CpuFlagNode:
                    targets.Add(assignment.Left);
                    break;
                case TupleSwapNode swap:
                    targets.Add(swap.First);
                    targets.Add(swap.Second);
                    break;
                case DivisionNode division:
                    targets.Add(division.Division.Low);
                    targets.Add(division.Division.High);
                    break;
                case MultiplicationNode multiplication:
                    targets.Add(multiplication.Low);
                    targets.Add(multiplication.High);
                    break;
                case InterruptServiceCallNode serviceCall:
                    foreach (ValueNode result in serviceCall.Results) {
                        targets.Add(result);
                    }
                    break;
            }
        }
    }

    private static void CollectReads(IVisitableAstNode root,
        Dictionary<IVisitableAstNode, string> table, HashSet<IVisitableAstNode> targets,
        Dictionary<IrSsaValue, TypedParameter> parameterByValue, int[] entryVersions, int[] usedVersions) {
        HashSet<IVisitableAstNode> keptRegisters = KeptTwinOperands(root, targets);
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            if (targets.Contains(node) || table.ContainsKey(node)
                || keptRegisters.Contains(node)
                || ValueOf(node) is not { } value
                || !parameterByValue.TryGetValue(value, out TypedParameter? parameter)) {
                continue;
            }
            int used = usedVersions[(int)value];
            if (used < 0 || used != entryVersions[(int)value]) {
                continue;
            }
            if (ReadText(node, parameter) is { } text) {
                table[node] = text;
            }
        }
    }

    /// <summary>The register operands of <paramref name="root"/>'s ALU calls that keep the register
    /// print: when one argument of an <see cref="AluCallValueNode"/> is the assignment-target
    /// instance itself, every other argument reading the same register bytes is the twin operand of
    /// <c>X = op(X, X)</c> and is not substituted.</summary>
    private static HashSet<IVisitableAstNode> KeptTwinOperands(IVisitableAstNode root,
        HashSet<IVisitableAstNode> targets) {
        HashSet<IVisitableAstNode> kept = new(ReferenceEqualityComparer.Instance);
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            IReadOnlyList<IVisitableAstNode>? arguments = node switch {
                AluOperationNode alu => alu.Call.CallNode.Arguments,
                AluStatementNode aluStatement => aluStatement.Call.Arguments,
                AluCallValueNode call => call.CallNode.Arguments,
                _ => null
            };
            if (arguments is null) {
                continue;
            }
            foreach (IVisitableAstNode argument in arguments) {
                if (argument is not RegisterNode register || !targets.Contains(argument)) {
                    continue;
                }
                foreach (IVisitableAstNode other in arguments) {
                    if (!ReferenceEquals(other, argument) && other is RegisterNode otherRegister
                        && IrValueSets.Of(otherRegister) == IrValueSets.Of(register)) {
                        kept.Add(other);
                    }
                }
            }
        }
        return kept;
    }

    /// <summary>The parameter text a read of <paramref name="node"/> substitutes with: the parameter
    /// name for an exact-width read, a cast for a narrower one, <c>null</c> when the read is wider
    /// than the parameter (it keeps the register).</summary>
    private static string? ReadText(IVisitableAstNode node, TypedParameter parameter) {
        if (node is SegmentRegisterNode or CpuFlagNode) {
            return parameter.Name;
        }
        ulong readMask = (ulong)IrValueSets.Of((RegisterNode)node);
        ulong parameterMask = (ulong)parameter.Mask;
        if ((readMask & ~parameterMask) != 0) {
            return null;
        }
        if (readMask == parameterMask) {
            return parameter.Name;
        }
        int firstLane = System.Numerics.BitOperations.TrailingZeroCount(readMask)
            - System.Numerics.BitOperations.TrailingZeroCount(parameterMask) / 4 * 4;
        return (firstLane, readMask >> System.Numerics.BitOperations.TrailingZeroCount(readMask)) switch {
            (0, 0x1) => $"(byte){parameter.Name}",
            (1, 0x1) => $"(byte)({parameter.Name} >> 8)",
            (0, _) => $"(ushort){parameter.Name}",
            _ => $"(ushort)({parameter.Name} >> 16)"
        };
    }

    /// <summary>The <see cref="IrSsaValue"/> a read of <paramref name="node"/> belongs to:
    /// <c>null</c> for a node that is not a register or single flag read.</summary>
    internal static IrSsaValue? ValueOf(IVisitableAstNode node) => node switch {
        SegmentRegisterNode segment => (IrSsaValue)((int)IrSsaValue.Es + segment.RegisterIndex),
        RegisterNode register => (IrSsaValue)(
            System.Numerics.BitOperations.TrailingZeroCount((ulong)IrValueSets.Of(register)) / 4),
        CpuFlagNode flag => ValueOfFlag(flag.FlagMask),
        _ => null
    };

    internal static IrSsaValue? ValueOf(CpuValues mask) {
        ulong bits = (ulong)mask;
        int trailing = System.Numerics.BitOperations.TrailingZeroCount(bits);
        if (trailing < 32) {
            return (IrSsaValue)(trailing / 4);
        }
        if (trailing < 38) {
            return (IrSsaValue)((int)IrSsaValue.Es + trailing - 32);
        }
        return (IrSsaValue)((int)IrSsaValue.Carry + trailing - 38);
    }

    /// <summary>The parameters of <paramref name="signature"/> that keep their copy-in line: a
    /// parameter is copied into its register only while something the printed method runs can
    /// still read the register's entry value. The worklist follows the parameter's bytes from the
    /// entry version through phis and the carried bytes of partial definitions, and a copy-in
    /// stays when it meets a use site whose read the printed parameter text does not cover, a
    /// site that may fault, an exit that is not a <see cref="FailAsUntestedNode"/> (a typed return
    /// only when the bytes meet an output mask), or a machine-register print outside every IR
    /// site - a node the plans created, such as a saved-pair initializer. <paramref name="hasFrame"/>
    /// keeps an SS parameter: the frame reads <c>State.SegmentRegisters</c>.</summary>
    internal static IReadOnlyList<TypedParameter> CopyInParameters(IrMethod method, IrSsa ssa,
        TypedSignature signature, IReadOnlySet<IVisitableAstNode> printedParameterReads,
        IReadOnlySet<IVisitableAstNode> printedMachineRegisters, bool hasFrame,
        IReadOnlySet<TypedParameter> promotedEntryParameters) {
        HashSet<IVisitableAstNode> irSiteNodes = new(ReferenceEqualityComparer.Instance);
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                irSiteNodes.UnionWith(IrAstChildren.Descendants(statement.Node));
            }
            irSiteNodes.UnionWith(IrAstChildren.TerminatorNodes(block.Terminator));
        }
        CpuValues outputMask = signature.Outputs.Aggregate(CpuValues.None,
            (mask, output) => mask | output.Mask);
        List<TypedParameter> copyIns = [];
        foreach (TypedParameter parameter in signature.Parameters) {
            if (NeedsCopyIn(ssa, signature, parameter, outputMask, printedParameterReads,
                    printedMachineRegisters, irSiteNodes, hasFrame,
                    promotedEntryParameters.Contains(parameter))) {
                copyIns.Add(parameter);
            }
        }
        return copyIns;
    }

    private static bool NeedsCopyIn(IrSsa ssa, TypedSignature signature, TypedParameter parameter,
        CpuValues outputMask, IReadOnlySet<IVisitableAstNode> printedParameterReads,
        IReadOnlySet<IVisitableAstNode> printedMachineRegisters,
        HashSet<IVisitableAstNode> irSiteNodes, bool hasFrame, bool promotedEntryParameter) {
        if (ValueOf(parameter.Mask) is not { } value) {
            return true;
        }
        CpuValues parameterBytes = parameter.Mask;
        if (hasFrame && (parameterBytes & CpuValues.Ss) != CpuValues.None) {
            return true;
        }
        int entryVersion = ssa.MethodEntryVersion(signature.EntryBlock, value);
        if (promotedEntryParameter) {
            // The entry version promotes to the parameter's own local: no IR site still reads the
            // register's entry value. Only a machine-register print the plans created outside
            // every IR site (a saved-pair initializer) can still need the copy-in.
            return printedMachineRegisters.Any(node => !irSiteNodes.Contains(node)
                && (MachineMaskOf(node) & parameterBytes) != CpuValues.None);
        }
        HashSet<(int Version, CpuValues Bytes)> visited = [];
        Queue<(int Version, CpuValues Bytes)> worklist = new();
        worklist.Enqueue((entryVersion, parameterBytes));
        while (worklist.Count > 0) {
            (int version, CpuValues bytes) = worklist.Dequeue();
            if (!visited.Add((version, bytes))) {
                continue;
            }
            foreach (IrSsaUse use in ssa.Uses(version)) {
                if (use.Phi is { } phi) {
                    worklist.Enqueue((phi.Result, bytes));
                    continue;
                }
                if (use.Terminator is IrExit exit) {
                    if (NeedsCopyInAtExit(exit, bytes, outputMask)) {
                        return true;
                    }
                    continue;
                }
                object? site = use.Statement ?? (object?)use.Terminator;
                if (site is not (IrStatement or IrTerminator)) {
                    return true;
                }
                IrEffects effects = site is IrStatement statement
                    ? statement.Effects : ((IrTerminator)site).Effects;
                if (effects.MayFault) {
                    return true;
                }
                CpuValues read = effects.Reads & bytes;
                if (version == entryVersion) {
                    read &= ~CoveredReads(site, value, printedParameterReads);
                }
                if (read != CpuValues.None) {
                    return true;
                }
                IReadOnlyList<(IrSsaValue Value, int Version)> definitions = site is IrStatement siteStatement
                    ? ssa.Definitions(siteStatement) : ssa.Definitions((IrTerminator)site);
                foreach ((IrSsaValue defined, int newVersion) in definitions) {
                    if (defined != value) {
                        continue;
                    }
                    CpuValues carried = bytes & ~effects.MustWrite;
                    if (carried != CpuValues.None) {
                        worklist.Enqueue((newVersion, carried));
                    }
                }
            }
        }
        foreach (IVisitableAstNode node in printedMachineRegisters) {
            if (!irSiteNodes.Contains(node) && (MachineMaskOf(node) & parameterBytes) != CpuValues.None) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the exit use of the followed bytes needs the machine register to hold the
    /// entry value: a <see cref="FailAsUntestedNode"/> aborts the program so nothing observes it,
    /// a typed return needs the bytes an output covers, and every other exit uses it.</summary>
    private static bool NeedsCopyInAtExit(IrExit exit, CpuValues bytes, CpuValues outputMask) =>
        exit.Transfer switch {
            FailAsUntestedNode => false,
            ReturnNode { Value: ProcedureReturnHelperNode } => (bytes & outputMask) != CpuValues.None,
            _ => true
        };

    /// <summary>The read bytes of <paramref name="site"/> the printed body covers by printing a
    /// parameter instead of the register: the union of the masks of the site's descendant nodes
    /// that printed with <paramref name="printedParameterReads"/> text and read
    /// <paramref name="value"/>.</summary>
    private static CpuValues CoveredReads(object site, IrSsaValue value,
        IReadOnlySet<IVisitableAstNode> printedParameterReads) {
        IEnumerable<IVisitableAstNode> descendants = site is IrStatement statement
            ? IrAstChildren.Descendants(statement.Node)
            : IrAstChildren.TerminatorNodes((IrTerminator)site);
        CpuValues covered = CpuValues.None;
        foreach (IVisitableAstNode node in descendants) {
            if (printedParameterReads.Contains(node) && ValueOf(node) == value) {
                covered |= MachineMaskOf(node);
            }
        }
        return covered;
    }

    /// <summary>The <see cref="CpuValues"/> a read of <paramref name="node"/> covers.</summary>
    private static CpuValues MachineMaskOf(IVisitableAstNode node) {
        if (node is RegisterNode register) {
            return IrValueSets.Of(register);
        }
        return ValueOf(node) is { } value ? IrSsaValues.MaskOf(value) : CpuValues.None;
    }

    private static IrSsaValue? ValueOfFlag(ushort flagMask) => flagMask switch {
        Spice86.Core.Emulator.CPU.Flags.Carry => IrSsaValue.Carry,
        Spice86.Core.Emulator.CPU.Flags.Parity => IrSsaValue.Parity,
        Spice86.Core.Emulator.CPU.Flags.Auxiliary => IrSsaValue.Auxiliary,
        Spice86.Core.Emulator.CPU.Flags.Zero => IrSsaValue.Zero,
        Spice86.Core.Emulator.CPU.Flags.Sign => IrSsaValue.Sign,
        Spice86.Core.Emulator.CPU.Flags.Overflow => IrSsaValue.Overflow,
        Spice86.Core.Emulator.CPU.Flags.Direction => IrSsaValue.Direction,
        _ => null
    };
}
