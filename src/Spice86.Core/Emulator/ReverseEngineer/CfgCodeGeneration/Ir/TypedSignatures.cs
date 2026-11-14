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

    /// <summary>The signature a call statement may use: <paramref name="node"/> is a 16-bit call
    /// whose only observed target is a typed method's entry, with no redirected return, no observed
    /// mid-call landings and an observed continuation; <c>null</c> for any other call site (it keeps
    /// the untyped helper).</summary>
    public TypedSignature? TypedCallOf(ProcedureCallHelperNode node) {
        if (node.SingleObservedTarget && node.ObservedContinuation && !node.ObservedLandings
            && node.Width == BitWidth.WORD_16
            && node.Arguments[^1] is MethodGroupNode { SingleEntry: true, LoadOffset: 0 } group
            && _byMethodName.TryGetValue(group.MethodName, out TypedSignature? signature)) {
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
        signatures.AssignNames(methods);
        return signatures;
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
            prepared.FramePlan);
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
        if (!ExitsAreTyped(method, out CallDistance distance, out int poppedBytes)) {
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
        if (proved is null || proved.Distance != distance || proved.PoppedBytes != poppedBytes) {
            return null;
        }
        Dictionary<string, int> nameCounters = new(StringComparer.Ordinal);
        return new TypedSignature {
            Parameters = GenericNames(CpuValueParameters.Decompose(inputs), nameCounters),
            Outputs = GenericNames(CpuValueParameters.Decompose(outputs), nameCounters),
            Distance = distance,
            PoppedBytes = poppedBytes,
            EntryBlock = irEntry.Block
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

    /// <summary>Every exit of <paramref name="method"/> is a 16-bit <c>ret</c> / <c>retf</c> of the
    /// same distance popping the same byte count, or an abort (<c>FailAsUntested</c>, throw); at
    /// least one is a return.</summary>
    private static bool ExitsAreTyped(IrMethod method, out CallDistance distance, out int poppedBytes) {
        distance = CallDistance.Near;
        poppedBytes = 0;
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
            if (nodes.Any(node => node.GetType() == typeof(MethodCallNode))) {
                return false;
            }
            List<ProcedureReturnHelperNode> returnHelpers = nodes.OfType<ProcedureReturnHelperNode>().ToList();
            if (returnHelpers.Count > 0) {
                foreach (ProcedureReturnHelperNode helper in returnHelpers) {
                    int popCount = PopCount(helper);
                    if (helper.Width != BitWidth.WORD_16 || popCount < 0
                        || (returns > 0 && (helper.Distance != distance || popCount != poppedBytes))) {
                        return false;
                    }
                    distance = helper.Distance;
                    poppedBytes = popCount;
                    returns++;
                }
                continue;
            }
            if (!nodes.Any(node => node is FailAsUntestedNode or ThrowNode or ReturnNode)) {
                return false;
            }
        }
        return returns > 0;
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
    /// failure. Only a typed <see cref="InterruptServiceCallNode"/> that does not write the
    /// interrupt frame (<see cref="InterruptServiceCallNode.WritesFrame"/>) stays allowed: it
    /// does neither on a normal return and keeps its own statement effects (IS-8). A
    /// frame-writing call or a <see cref="InterruptSiteKind.BuiltInDispatch"/> site pushes the
    /// FLAGS image, which would put six status-flag parameters only the frame reads on the
    /// signature - worse than the untyped method, so those wait for flags as values.</summary>
    private static bool HasInterruptOrCallback(IrMethod method) =>
        method.Blocks.SelectMany(block =>
                block.Statements.Select(statement => statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator)))
            .Any(root => IrAstChildren.Descendants(root).Any(node =>
                node is ProgramInterruptCallHelperNode
                    or InterruptCallHelperNode
                    or InterruptCallReturnedElsewhereHelperNode
                    or CallbackHelperNode or CallbackMovedCsIpHelperNode
                    || node is InterruptServiceCallNode { WritesFrame: true }));

    /// <summary>
    /// The read-position register / flag occurrences of <paramref name="method"/> that print as
    /// parameters: for each statement, each <see cref="RegisterNode"/> / <see cref="SegmentRegisterNode"/>
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
