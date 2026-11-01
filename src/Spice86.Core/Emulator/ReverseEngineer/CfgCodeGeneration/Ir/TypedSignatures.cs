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
    /// whose only observed target is a typed method's entry, with no redirected return and an
    /// observed continuation; <c>null</c> for any other call site (it keeps the untyped helper).</summary>
    public TypedSignature? TypedCallOf(ProcedureCallHelperNode node) {
        if (node.SingleObservedTarget && node.ObservedContinuation && node.Width == BitWidth.WORD_16
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
        IrCallingConventions conventions, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder) {
        Dictionary<string, TypedSignature> byName = methods
            .Select(source => (source.Method.MethodName, Signature: SignatureOf(source, conventions, context, effectBuilder)))
            .Where(pair => pair.Signature is not null)
            .ToDictionary(pair => pair.MethodName,
                pair => pair.Signature ?? throw new InvalidOperationException("SignatureOf gave no signature."));
        return new TypedSignatures(byName);
    }

    private static TypedSignature? SignatureOf(PreparedMethod source,
        IrCallingConventions conventions, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder) {
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
        if (!ReturnsToCaller(method, source.Ssa, irEntry, distance, plan.MethodName, conventions,
                effectBuilder)) {
            return null;
        }
        return new TypedSignature {
            Parameters = CpuValueParameters.Decompose(inputs),
            Outputs = CpuValueParameters.Decompose(outputs),
            Distance = distance,
            PoppedBytes = poppedBytes,
            EntryBlock = irEntry.Block
        };
    }

    /// <summary>Whether <paramref name="method"/> structurally returns to its caller: every return
    /// exit reads the stacked return address the caller pushed. Holds when SP at the exit is at
    /// entry delta 0 (the <see cref="IrFrames.EntryDelta(IrStackOffset)"/> rule, followed only
    /// through statements that write neither SP nor SS) or the exit block's epilogue is
    /// <see cref="IrFrameEpilogueKind.MoveSpPopBp"/> / <see cref="IrFrameEpilogueKind.Leave"/> of a
    /// recognized standard frame (the epilogue itself restores SP to entry delta 0), and no proved
    /// frame access writes the return frame <c>[0, r)</c> (the method's own return address, saved
    /// CS or saved FLAGS). A callee that rewrote its return slot or returns with the stack at
    /// another level is not typed: the typed-call check would fail where an untyped call unwinds.
    /// </summary>
    private static bool ReturnsToCaller(IrMethod method, IrSsa ssa, IrEntry entry,
        CallDistance distance, string methodName, IrCallingConventions conventions,
        HardwareInterruptEffectBuilder effectBuilder) {
        IrStackSlots slots = conventions.SlotsOf(methodName);
        // IrStackSlots.Empty means the method holds no push / pop / peek node: SP then stays at its
        // entry delta unless a statement writes SP / SS by another route (for example mov sp,ax).
        bool writesStack = method.Blocks.SelectMany(block => block.Statements).Any(statement =>
            ((statement.Effects.MustWrite | statement.Effects.MayWrite)
                & (CpuValues.Esp | CpuValues.Ss)) != CpuValues.None);
        bool untracked = ReferenceEquals(slots, IrStackSlots.Empty);
        if (untracked && writesStack) {
            return false;
        }
        IrFrames frames = IrFrames.Compute(method, ssa, slots,
            statement => {
                if (statement.Origin is CfgInstruction instruction) {
                    return effectBuilder.CallTargetWrites(instruction);
                }
                return CpuValues.Registers;
            });
        int returnBytes = distance == CallDistance.Near ? 2 : 4;
        if (frames.Accesses.Any(access =>
                access.IsWrite && access.EntryOffset >= 0 && access.EntryOffset < returnBytes)) {
            return false;
        }
        IrFrame? frame = frames.FrameAt(entry);
        foreach (IrBlock block in method.Blocks) {
            if (block.Terminator is not IrExit exit || !IsReturnExit(exit)) {
                continue;
            }
            if (untracked || frames.EntryDelta(slots.OffsetAtBlockEnd(block)) == 0) {
                continue;
            }
            if (frame?.Epilogues.Any(epilogue => epilogue.Exit == exit
                    && epilogue.Kind is IrFrameEpilogueKind.MoveSpPopBp
                        or IrFrameEpilogueKind.Leave) == true) {
                continue;
            }
            return false;
        }
        return true;
    }

    private static bool IsReturnExit(IrExit exit) =>
        IrAstChildren.Descendants(exit.Transfer)
            .OfType<ProcedureReturnHelperNode>().Any();

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
    /// <c>int</c> or a callback: either can end the program or move CS:IP, which the typed return
    /// check would turn into a failure.</summary>
    private static bool HasInterruptOrCallback(IrMethod method) =>
        method.Blocks.SelectMany(block =>
                block.Statements.Select(statement => statement.Node)
                    .Concat(IrAstChildren.RootsOf(block.Terminator)))
            .Any(root => IrAstChildren.Descendants(root).Any(node =>
                node is InterruptCallHelperNode or ProgramInterruptCallHelperNode
                    or InterruptCallReturnedElsewhereHelperNode or InterruptServiceCallNode
                    or CallbackHelperNode or CallbackMovedCsIpHelperNode));

    /// <summary>
    /// The read-position register / flag occurrences of <paramref name="method"/> that print as
    /// parameters: for each statement, each <see cref="RegisterNode"/> / <see cref="SegmentRegisterNode"/>
    /// / single-flag <see cref="CpuFlagNode"/> occurrence whose used version is the entry version,
    /// keyed by node reference. An assignment target is never substituted: shared node objects can
    /// sit on both sides of <c>X = X op y</c>, so the write stays on the register.
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
            entryVersions[(int)value] = ssa.VersionBefore(signature.EntryBlock, 0, value);
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
    /// node that is the left side of an assignment, a swap operand, or a division result.</summary>
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
            }
        }
    }

    private static void CollectReads(IVisitableAstNode root,
        Dictionary<IVisitableAstNode, string> table, HashSet<IVisitableAstNode> targets,
        Dictionary<IrSsaValue, TypedParameter> parameterByValue, int[] entryVersions, int[] usedVersions) {
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(root)) {
            if (targets.Contains(node) || table.ContainsKey(node)
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
    private static IrSsaValue? ValueOf(IVisitableAstNode node) => node switch {
        SegmentRegisterNode segment => (IrSsaValue)((int)IrSsaValue.Es + segment.RegisterIndex),
        RegisterNode register => (IrSsaValue)(
            System.Numerics.BitOperations.TrailingZeroCount((ulong)IrValueSets.Of(register)) / 4),
        CpuFlagNode flag => ValueOfFlag(flag.FlagMask),
        _ => null
    };

    private static IrSsaValue? ValueOf(CpuValues mask) {
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
