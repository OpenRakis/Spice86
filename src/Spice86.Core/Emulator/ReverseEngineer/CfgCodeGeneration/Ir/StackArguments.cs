namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// The strict-tier stack-argument plan: for each typed method whose argument slots are
/// provably private, the reads of those slots print as C# parameters and every typed call site
/// folds its matched argument pushes (and dead-flag <c>add sp,n</c>) into
/// <c>CallWithStackArguments</c>. A method's slots are private when every read of them is a proven
/// <see cref="IrFrames"/> access, nothing writes the argument area inside the method (no store
/// that is not a proven frame write below the return address, no call or delivered handler that
/// writes above its own frame, no SS passed on as a data segment), the entry is reached only by
/// generated calls, and every typed call site pushes exactly the slots. Any failed condition keeps
/// today's memory form.
/// </summary>
internal sealed class StackArguments {
    private static readonly CpuValues StackValues = CpuValues.Esp | CpuValues.Ss;

    private readonly Dictionary<string, StackArgumentPlan> _plans = new(StringComparer.Ordinal);
    private readonly Dictionary<ProcedureCallHelperNode, StackCallMerge> _merges =
        new(ReferenceEqualityComparer.Instance);
    private readonly CfgGeneratorContext _context;
    private readonly HardwareInterruptEffectBuilder _effectBuilder;
    private readonly ProgramImages _images;
    private readonly Dictionary<ICfgNode, CfgCodePartitionEntry> _entryByNode = [];
    private readonly Dictionary<CfgCodePartitionEntry, PreparedMethod> _methodByEntry = [];
    private readonly Dictionary<ICfgNode, PreparedMethod> _methodByNode =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IrFrames> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _aboveFrame = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deciding = new(StringComparer.Ordinal);
    private readonly List<(SegmentedAddress Address, int ArgumentBytes)> _spaceCallSites = [];
    private List<CfgCodePartitionEntry>? _handlerRoots;

    private StackArguments(CfgGeneratorContext context, HardwareInterruptEffectBuilder effectBuilder,
        ProgramImages images) {
        _context = context;
        _effectBuilder = effectBuilder;
        _images = images;
    }

    /// <summary>One stack argument of a method: the parameter name (<c>arg0</c>, <c>arg2</c>, ...),
    /// the slot's offset from the entry SP and its width in bytes.</summary>
    internal sealed record StackParameter(string Name, int EntryOffset, int Width) {
        /// <summary>The C# parameter type.</summary>
        internal string Type => Width switch {
            1 => "byte",
            4 => "uint",
            _ => "ushort"
        };

        /// <summary>The frame read the <c>_Asm</c> wrapper performs to pass the slot value.</summary>
        internal string WrapperRead => Width switch {
            1 => $"new GeneratedStackFrame(Memory, State.SegmentRegisters, SP).GetByte({EntryOffset})",
            4 => $"new GeneratedStackFrame(Memory, State.SegmentRegisters, SP).GetDword({EntryOffset})",
            _ => $"new GeneratedStackFrame(Memory, State.SegmentRegisters, SP).GetWord({EntryOffset})"
        };
    }

    /// <summary>The plan of one callee: the parameters and the pointer nodes that print as them.</summary>
    internal sealed record StackArgumentPlan(IReadOnlyList<StackParameter> Parameters,
        IReadOnlyDictionary<SegmentedPointerNode, string> ArgumentReads);

    /// <summary>The caller-side merge of one call site: the push instructions to fold (in program
    /// order, keyed by origin), the argument expressions in callee slot order and the clean-up the
    /// helper performs.</summary>
    internal sealed record StackCallMerge(IReadOnlyList<ICfgNode> PushOrigins,
        IReadOnlyList<ValueNode> Arguments, ICfgNode? CleanupOrigin, int CleanupBytes,
        TypedSignature Signature, string MethodName, SegmentedAddress CallAddress);

    /// <summary>The parameter plan of <paramref name="methodName"/>, or null when the method keeps
    /// memory reads for its argument slots.</summary>
    internal StackArgumentPlan? PlanOf(string methodName) =>
        _plans.TryGetValue(methodName, out StackArgumentPlan? plan) ? plan : null;

    /// <summary>The merge planned for the call site <paramref name="call"/> prints, or null when it
    /// keeps the push / call / <c>add sp</c> form.</summary>
    internal StackCallMerge? MergeFor(ProcedureCallHelperNode call) =>
        _merges.TryGetValue(call, out StackCallMerge? merge) ? merge : null;

    /// <summary>The generation policy this analysis planned under, shared by every consumer.</summary>
    internal GenerationPolicy Policy => _context.Policy;

    /// <summary>The call instruction address and reserved argument bytes of every merge that
    /// folded while the abandoned-stack-slots relaxation was enabled: the sites whose printed
    /// <c>CallWithStackSpace</c> skips the argument-byte writes. Empty under strict mode.</summary>
    internal IReadOnlyList<(SegmentedAddress Address, int ArgumentBytes)> SpaceCallSites =>
        _spaceCallSites;

    /// <summary>Computes the plan of every method of the program, after the typed signatures.</summary>
    public static StackArguments Compute(IReadOnlyList<PreparedMethod> methods,
        TypedSignatures signatures, CfgGeneratorContext context,
        HardwareInterruptEffectBuilder effectBuilder, ProgramImages images) {
        StackArguments arguments = new(context, effectBuilder, images);
        foreach (PreparedMethod prepared in methods) {
            foreach (CfgCodePartitionEntry entry in context.GetEntries(prepared.Method.Partition)) {
                arguments._entryByNode[entry.Node] = entry;
                arguments._methodByEntry[entry] = prepared;
                arguments._methodByNode[entry.Node] = prepared;
            }
        }
        foreach (PreparedMethod prepared in methods) {
            TypedSignature? signature = signatures.Of(prepared.Method.MethodName);
            if (signature is null) {
                continue;
            }
            if (prepared.Method.Entries.Count != 1) {
                continue;
            }
            if (arguments.PlanCallee(prepared, signature,
                    context.GetEntries(prepared.Method.Partition)[0]) is { } plan) {
                arguments._plans[prepared.Method.MethodName] = plan;
            }
        }
        foreach (PreparedMethod prepared in methods) {
            arguments.PlanCallers(prepared, signatures);
        }
        return arguments;
    }

    private StackArgumentPlan? PlanCallee(PreparedMethod prepared, TypedSignature signature,
        CfgCodePartitionEntry entry) {
        if (entry.Kind != CfgCodePartitionEntryKind.FunctionEntry) {
            return null;
        }
        IrMethod method = prepared.IrMethod;
        IrFrames frames = FramesOf(prepared);
        if (frames.ReturnAddressBytes != 2) {
            return null;
        }
        int returnBytes = 2;
        List<(int Offset, int Width)> slots = [.. frames.Accesses
            .Where(access => !access.IsWrite && access.EntryOffset >= returnBytes)
            .Select(access => (Offset: access.EntryOffset, Width: access.Width))
            .Distinct()
            .OrderBy(slot => slot.Offset)];
        if (slots.Count == 0) {
            return null;
        }
        if (slots.Any(slot => slot.Width != 2)) {
            return null;
        }
        int expected = returnBytes;
        foreach ((int offset, int width) in slots) {
            if (offset != expected) {
                return null;
            }
            expected += width;
        }
        foreach (IrFrameAccess access in frames.Accesses.Where(access => access.IsWrite)) {
            if (access.EntryOffset >= returnBytes) {
                return null;
            }
        }
        HashSet<IrBlock> readBlocks = [.. frames.Accesses
            .Where(access => access.EntryOffset >= returnBytes)
            .Select(access => access.Statement)
            .Select(statement => method.Blocks.Single(
                block => block.Statements.Contains(statement)))];
        HashSet<IrBlock> windowBlocks = WindowBlocks(method, readBlocks);
        if (CheckWindow(prepared, frames, windowBlocks, readBlocks, returnBytes) is not null) {
            return null;
        }
        List<StackParameter> parameters = [.. slots
            .Select(slot => new StackParameter($"arg{slot.Offset - returnBytes}",
                slot.Offset, slot.Width))];
        Dictionary<SegmentedPointerNode, List<IrFrameAccess>> byNode =
            new(ReferenceEqualityComparer.Instance);
        foreach ((SegmentedPointerNode pointer, IrFrameAccess? access) in frames.PointerResults) {
            if (access is null) {
                continue;
            }
            if (!byNode.TryGetValue(pointer, out List<IrFrameAccess>? list)) {
                list = [];
                byNode[pointer] = list;
            }
            list.Add(access);
        }
        Dictionary<SegmentedPointerNode, string> argumentReads = new(ReferenceEqualityComparer.Instance);
        foreach ((SegmentedPointerNode pointer, List<IrFrameAccess> accesses) in byNode) {
            if (accesses.All(access => !access.IsWrite && access.EntryOffset >= returnBytes
                    && accesses[0].EntryOffset == access.EntryOffset
                    && accesses[0].Width == access.Width)
                    && accesses[0].Width == 2) {
                StackParameter parameter = parameters
                    .Single(parameter => parameter.EntryOffset == accesses[0].EntryOffset);
                argumentReads[pointer] = parameter.Name;
            }
        }
        return new StackArgumentPlan(parameters, argumentReads);
    }

    /// <summary>The blocks that can run before a slot read: the read blocks plus every block that
    /// can reach one.</summary>
    private static HashSet<IrBlock> WindowBlocks(IrMethod method, HashSet<IrBlock> readBlocks) {
        HashSet<IrBlock> window = new(readBlocks, ReferenceEqualityComparer.Instance);
        Queue<IrBlock> pending = new(readBlocks);
        while (pending.Count > 0) {
            IrBlock block = pending.Dequeue();
            foreach (IrBlock predecessor in method.Blocks.Where(candidate =>
                    candidate.Terminator.Edges.Any(edge => ReferenceEquals(edge.Target, block)))
                    .Where(window.Add)) {
                pending.Enqueue(predecessor);
            }
        }
        return window;
    }

    private static int LastReadIndex(IrBlock block, IrFrames frames) {
        int last = -1;
        for (int index = 0; index < block.Statements.Count; index++) {
            if (frames.AccessesOf(block.Statements[index])
                    .Any(access => !access.IsWrite && access.EntryOffset >= 0)) {
                last = index;
            }
        }
        return last;
    }

    /// <summary>Refuses when anything inside the window can write the argument area: an unproven
    /// store, an above-frame call target or handler, or SS passed on as a data segment.</summary>
    private string? CheckWindow(PreparedMethod prepared, IrFrames frames, HashSet<IrBlock> windowBlocks,
        HashSet<IrBlock> readBlocks, int returnBytes) {
        foreach (IrBlock block in windowBlocks) {
            bool canReachLaterRead = block.Terminator.Edges.Any(edge => windowBlocks.Contains(edge.Target));
            int limit = readBlocks.Contains(block) && !canReachLaterRead
                ? LastReadIndex(block, frames) : block.Statements.Count - 1;
            for (int index = 0; index <= limit && index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (statement.Node is CheckExternalEventsNode
                        && HandlerWritesAboveFrame() is { } handlerReason) {
                    return handlerReason;
                }
                if (statement.Origin is CfgInstruction { IsCall: true } call
                        && CallWritesAboveFrame(call) is { } callReason) {
                    return callReason;
                }
                if (CheckStatement(statement, frames, returnBytes) is { } reason) {
                    return reason;
                }
            }
        }
        return null;
    }

    /// <summary>The per-statement window checks: a store must be a proven frame write inside the
    /// frame, no interrupt delivery, no SS copy into a data segment.</summary>
    private static string? CheckStatement(IrStatement statement, IrFrames frames, int returnBytes) {
        ValueNode? storeTarget = null;
        if (statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN,
                Left: SegmentedPointerNode or AbsolutePointerNode } assign) {
            storeTarget = assign.Left;
        }
        if (storeTarget is AbsolutePointerNode) {
            return "unproven memory write";
        }
        foreach (IVisitableAstNode node in IrAstChildren.Descendants(statement.Node)) {
            switch (node) {
                case InterruptCallHelperNode or ProgramInterruptCallHelperNode
                    or InterruptCallReturnedElsewhereHelperNode or InterruptServiceCallNode
                    or CallbackHelperNode or CallbackMovedCsIpHelperNode:
                    return "interrupt delivery inside the argument window";
                case BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: SegmentRegisterNode }:
                    return "a segment register is written inside the argument window";
            }
        }
        if (storeTarget is SegmentedPointerNode storePointer) {
            IrFrameAccess? access = frames.PointerResults
                .Where(result => ReferenceEquals(result.Pointer, storePointer))
                .Select(result => result.Access)
                .FirstOrDefault();
            if (access is null) {
                return "unproven memory write";
            }
            if (access.EntryOffset >= returnBytes) {
                return "method writes an argument slot";
            }
        }
        return null;
    }

    private string? CallWritesAboveFrame(CfgInstruction call) {
        foreach (ICfgNode target in HardwareInterruptEffectBuilder.TransferTargets(call)) {
            if (!_methodByNode.TryGetValue(target, out PreparedMethod? targetMethod)) {
                return "call target unknown for the above-frame check";
            }
            if (MayWriteAboveFrame(targetMethod)) {
                return "call target writes above its own frame";
            }
        }
        return null;
    }

    private string? HandlerWritesAboveFrame() {
        HardwareInterruptSetup setup = _context.Program.Inputs.HardwareInterrupts;
        if (setup.MouseUserSubroutineRegistered) {
            return "mouse user subroutine may write above its frame";
        }
        foreach (CfgCodePartitionEntry entry in HandlerRoots()) {
            if (_images.IsEmulatorRoutine(entry.Address.Linear)) {
                continue;
            }
            if (!_methodByEntry.TryGetValue(entry, out PreparedMethod? handler)) {
                return "a hardware handler is not generated";
            }
            if (MayWriteAboveFrame(handler)) {
                return "a hardware handler writes above its own frame";
            }
        }
        return null;
    }

    private List<CfgCodePartitionEntry> HandlerRoots() =>
        _handlerRoots ??= [.. _context.Program.Partitions
            .SelectMany(partition => _context.GetEntries(partition)
                .Where(entry => entry.Kind == CfgCodePartitionEntryKind.ExecutionContextEntry
                    && !entry.Address.Equals(_context.Program.Inputs.ProgramEntryPoint)))];

    /// <summary>Whether <paramref name="method"/> can write at or above its own entry SP on some
    /// path: an unproven store, a proven frame write at a non-negative entry offset, or a call /
    /// delivered handler that does. Memoized; a cycle answers true.</summary>
    private bool MayWriteAboveFrame(PreparedMethod method) {
        string name = method.Method.MethodName;
        if (_aboveFrame.TryGetValue(name, out bool cached)) {
            return cached;
        }
        if (!_deciding.Add(name)) {
            return true;
        }
        bool result = ComputeAboveFrame(method);
        _deciding.Remove(name);
        _aboveFrame[name] = result;
        return result;
    }

    private bool ComputeAboveFrame(PreparedMethod prepared) {
        IrMethod method = prepared.IrMethod;
        IrFrames frames = FramesOf(prepared);
        Dictionary<SegmentedPointerNode, IrFrameAccess?> byNode =
            new(ReferenceEqualityComparer.Instance);
        foreach ((SegmentedPointerNode pointer, IrFrameAccess? access) in frames.PointerResults) {
            if (!byNode.ContainsKey(pointer) || access is null) {
                byNode[pointer] = access;
            }
        }
        foreach (IrBlock block in method.Blocks) {
            foreach (IrStatement statement in block.Statements) {
                if (statement.Node is CheckExternalEventsNode
                        && HandlerWritesAboveFrame() is not null) {
                    return true;
                }
                if (statement.Origin is CfgInstruction { IsCall: true } call
                        && CallWritesAboveFrame(call) is not null) {
                    return true;
                }
                ValueNode? storeTarget = null;
                if (statement.Node is BinaryOperationNode {
                        BinaryOperation: BinaryOperation.ASSIGN,
                        Left: SegmentedPointerNode or AbsolutePointerNode } assign) {
                    storeTarget = assign.Left;
                }
                if (storeTarget is AbsolutePointerNode) {
                    return true;
                }
                if (storeTarget is SegmentedPointerNode pointer
                        && (!byNode.TryGetValue(pointer, out IrFrameAccess? access)
                            || access is null || access.EntryOffset >= 0)) {
                    return true;
                }
            }
        }
        return false;
    }

    private IrFrames FramesOf(PreparedMethod prepared) {
        if (_frames.TryGetValue(prepared.Method.MethodName, out IrFrames? frames)) {
            return frames;
        }
        IrMethod method = prepared.IrMethod;
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(method, prepared.Ssa, _context.Policy),
            _ => null);
        frames = IrFrames.Compute(method, prepared.Ssa, slots, statement => {
            if (statement.Origin is CfgInstruction instruction) {
                return _effectBuilder.CallTargetWrites(instruction);
            }
            return CpuValues.Registers;
        });
        _frames[prepared.Method.MethodName] = frames;
        return frames;
    }

    /// <summary>Plans the call sites: every typed call to a planned callee folds the pushes that
    /// feed its slots into <c>CallWithStackArguments</c>. A typed site whose pushes do not match
    /// refuses the callee's plan.</summary>
    private void PlanCallers(PreparedMethod prepared, TypedSignatures signatures) {
        foreach (IrBlock block in prepared.IrMethod.Blocks) {
            for (int index = 0; index < block.Statements.Count; index++) {
                IrStatement statement = block.Statements[index];
                if (statement.Origin is not CfgInstruction { IsCall: true } callInstruction) {
                    continue;
                }
                ProcedureCallHelperNode? call = IrAstChildren.Descendants(statement.Node)
                    .OfType<ProcedureCallHelperNode>().FirstOrDefault();
                if (call is null || signatures.TypedCallOf(call) is not { } signature
                        || signature.Distance != CallDistance.Near
                       
                        || call.Arguments[^1] is not MethodGroupNode group) {
                    continue;
                }
                if (!_plans.TryGetValue(group.MethodName, out StackArgumentPlan? plan)) {
                    continue;
                }
                StackCallMerge? merge = MatchPushes(prepared, block, index, plan, signature,
                    group.MethodName, callInstruction.Address);
                if (merge is null) {
                    _plans.Remove(group.MethodName);
                    foreach (ProcedureCallHelperNode earlier in _merges
                            .Where(pair => pair.Value.MethodName == group.MethodName)
                            .Select(pair => pair.Key).ToList()) {
                        _merges.Remove(earlier);
                    }
                    continue;
                }
                _merges[call] = merge;
            }
        }
    }

    /// <summary>The pushes feeding <paramref name="plan"/>'s slots: the last
    /// <c>Parameters.Count</c> pushes in the run ending at the call, over neutral statements only.
    /// Returns null when the run does not cover every slot.</summary>
    private StackCallMerge? MatchPushes(PreparedMethod prepared, IrBlock block, int callIndex,
        StackArgumentPlan plan, TypedSignature signature, string methodName,
        SegmentedAddress callAddress) {
        IReadOnlyList<IrStatement> statements = block.Statements;
        List<IrStatement> pushes = [];
        CpuValues blockedReads = CpuValues.None;
        for (int index = callIndex - 1; index >= 0 && pushes.Count < plan.Parameters.Count; index--) {
            IrStatement statement = statements[index];
            if (statement.Node is StackCallNode {
                    Operation: StackOperation.Push,
                    Arguments: [ValueNode pushed] }
                    && pushed.DataType.BitWidth == BitWidth.WORD_16) {
                if ((ReadsOf(pushed) & (blockedReads | StackValues)) != CpuValues.None
                        || IrAstChildren.Descendants(pushed).Any(node =>
                            node is SegmentedPointerNode or AbsolutePointerNode or MethodCallNode)) {
                    break;
                }
                pushes.Insert(0, statement);
                continue;
            }
            if (!Neutral(statement, out CpuValues writes)) {
                break;
            }
            blockedReads |= writes;
        }
        if (pushes.Count < plan.Parameters.Count) {
            return null;
        }
        List<ValueNode> arguments = [.. pushes
            .Select(push => (ValueNode)((StackCallNode)push.Node).Arguments[0])
            .Reverse()];
        ICfgNode? cleanupOrigin = null;
        int cleanupBytes = 0;
        int next = callIndex + 1;
        if (next < statements.Count
                && CleanupBytesOf(statements[next], prepared.Ssa, block, next) is { } cleanup) {
            cleanupOrigin = statements[next].Origin;
            cleanupBytes = cleanup;
        }
        return new StackCallMerge([.. pushes.Select(push => push.Origin)], arguments,
            cleanupOrigin, cleanupBytes, signature, methodName, callAddress);
    }

    /// <summary>The bytes of a dead-flag <c>add sp,n</c>, or null when the statement is not such a
    /// statement or its flags are still read.</summary>
    private static int? CleanupBytesOf(IrStatement statement, IrSsa ssa, IrBlock block, int index) {
        if (IrFlagLiveness.LiveAfter(ssa, block, index) != StatusFlags.None
                || statement.Node is not BinaryOperationNode {
                    BinaryOperation: BinaryOperation.ASSIGN,
                    Left: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex },
                    Right: { } right }) {
            return null;
        }
        IVisitableAstNode node = right;
        while (node is ExplicitCastNode cast) {
            node = cast.Value;
        }
        IVisitableAstNode? operand = null;
        if (node is AluOperationNode {
                Call: { Operation: AluOperation.Add,
                    CallNode.Arguments: [RegisterNode {
                            RegisterIndex: (int)RegisterIndex.SpIndex },
                        ValueNode aluAmount] } }) {
            operand = aluAmount;
        } else if (node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.PLUS,
                Left: RegisterNode { RegisterIndex: (int)RegisterIndex.SpIndex },
                Right: ValueNode amount }) {
            operand = amount;
        } else if (node is AluCallValueNode {
                Operation: AluOperation.Add,
                CallNode.Arguments: [RegisterNode {
                        RegisterIndex: (int)RegisterIndex.SpIndex },
                    ValueNode callAmount] }) {
            operand = callAmount;
        }
        if (operand is null) {
            return null;
        }
        return ConstantEvaluation.Resolved(operand) is ConstantNode { Value: <= ushort.MaxValue } constant
            ? (int)constant.Value : null;
    }

    /// <summary>Whether a statement between the pushes and the call cannot write the argument area
    /// or observe the stack: no memory access, no call or event delivery, no fault, no SP / SS
    /// read or write.</summary>
    private static bool Neutral(IrStatement statement, out CpuValues writes) {
        writes = CpuValues.None;
        IrEffects effects = statement.Effects;
        if (effects.MayFault || effects.DeliversInterrupts || effects.TouchesMemory) {
            return false;
        }
        if (IrAstChildren.Descendants(statement.Node).Any(node =>
                node is StackCallNode or CheckExternalEventsNode or SpeculativeGuardNode
                    or CpuFaultTryCatchNode
                    or MethodCallNode {
                        PropertyPath: not ("Alu8" or "Alu16" or "Alu32") })) {
            return false;
        }
        if (((effects.Reads | effects.MustWrite | effects.MayWrite) & StackValues)
                != CpuValues.None) {
            return false;
        }
        writes = effects.MustWrite | effects.MayWrite;
        return true;
    }

    /// <summary>The register / flag bytes <paramref name="node"/> reads: a push whose expression
    /// reads a byte a later statement writes cannot be evaluated at call time.</summary>
    private static CpuValues ReadsOf(ValueNode node) {
        CpuValues reads = CpuValues.None;
        foreach (IVisitableAstNode descendant in IrAstChildren.Descendants(node)) {
            switch (descendant) {
                case RegisterNode register:
                    reads |= IrValueSets.Of(register);
                    break;
                case CpuFlagNode flag:
                    reads |= IrValueSets.FromStatusFlags((StatusFlags)flag.FlagMask);
                    break;
            }
        }
        return reads;
    }

    /// <summary>Folds the merges whose push, call and clean-up boundaries all sit in one statement
    /// list of the structured <paramref name="body"/>: the push and clean-up boundaries go away and
    /// the call node becomes a <see cref="StackArgumentCallNode"/>. A call whose text appears in the
    /// body more than once, or whose folded boundaries did not structure together, keeps the push /
    /// call form - its pushes stay printed and it still passes the argument expressions, so both
    /// forms compute the same machine state.</summary>
    public BlockNode Apply(BlockNode body) {
        if (_merges.Count == 0) {
            return body;
        }
        Dictionary<ProcedureCallHelperNode, int> occurrences =
            new(ReferenceEqualityComparer.Instance);
        foreach (ProcedureCallHelperNode call in _merges.Keys) {
            occurrences[call] = 0;
        }
        CountCalls(body, occurrences);
        HashSet<InstructionBoundaryNode> removed = new(ReferenceEqualityComparer.Instance);
        Dictionary<ProcedureCallHelperNode, StackCallMerge> folded =
            new(ReferenceEqualityComparer.Instance);
        ScanBlock(body, occurrences, folded, removed);
        BlockNode rewritten = (BlockNode)Rewrite(body);
        return rewritten;

        IVisitableAstNode Rewrite(IVisitableAstNode node) {
            if (node is BlockNode block) {
                List<IVisitableAstNode> statements = [];
                foreach (IVisitableAstNode statement in block.Statements
                        .Where(statement => statement is not InstructionBoundaryNode boundary
                            || !removed.Remove(boundary))) {
                    statements.Add(Rewrite(statement));
                }
                return new BlockNode([.. statements]);
            }
            if (node is ProcedureCallHelperNode call
                    && folded.TryGetValue(call, out StackCallMerge? merge)) {
                return new StackArgumentCallNode(call,
                    [.. merge.Arguments], merge.CleanupBytes);
            }
            return IrAstChildren.MapChildren(node, Rewrite);
        }
    }

    private static void CountCalls(IVisitableAstNode node,
        Dictionary<ProcedureCallHelperNode, int> occurrences) {
        foreach (ProcedureCallHelperNode call in IrAstChildren.Descendants(node)
                .Prepend(node).OfType<ProcedureCallHelperNode>()
                .Where(occurrences.ContainsKey)) {
            occurrences[call]++;
        }
    }

    /// <summary>Scans every statement list: a merge folds only when its push and clean-up
    /// boundaries and the call boundary all appear in the same list.</summary>
    private void ScanBlock(IVisitableAstNode node,
        Dictionary<ProcedureCallHelperNode, int> occurrences,
        Dictionary<ProcedureCallHelperNode, StackCallMerge> folded,
        HashSet<InstructionBoundaryNode> removed) {
        if (node is BlockNode block) {
            Dictionary<ICfgNode, StackCallMerge> byOrigin = new(ReferenceEqualityComparer.Instance);
            foreach ((ProcedureCallHelperNode call, StackCallMerge merge) in _merges) {
                if (occurrences[call] != 1) {
                    continue;
                }
                foreach (ICfgNode origin in merge.PushOrigins) {
                    byOrigin[origin] = merge;
                }
                if (merge.CleanupOrigin is { } cleanup) {
                    byOrigin[cleanup] = merge;
                }
            }
            List<InstructionBoundaryNode> boundaries = [.. block.Statements
                .OfType<InstructionBoundaryNode>()];
            foreach (InstructionBoundaryNode boundary in boundaries) {
                foreach ((ProcedureCallHelperNode call, StackCallMerge candidate) in _merges) {
                    if (occurrences[call] != 1 || folded.ContainsKey(call)) {
                        continue;
                    }
                    if (!IrAstChildren.Descendants(boundary).Contains(call)) {
                        continue;
                    }
                    bool allFolded = candidate.PushOrigins
                        .Concat(candidate.CleanupOrigin is null
                            ? [] : [candidate.CleanupOrigin])
                        .All(origin => boundaries.Any(boundary =>
                            byOrigin.TryGetValue(boundary.Node, out StackCallMerge? owner)
                                && ReferenceEquals(owner, candidate)));
                    if (allFolded) {
                        folded[call] = candidate;
                        if (_context.Policy.AssumeAbandonedStackSlotsUnobserved) {
                            _spaceCallSites.Add((candidate.CallAddress,
                                candidate.Arguments.Count * 2));
                        }
                        foreach (InstructionBoundaryNode foldedBoundary in boundaries.Where(
                                boundary => byOrigin.TryGetValue(boundary.Node,
                                        out StackCallMerge? owner)
                                    && ReferenceEquals(owner, candidate))) {
                            removed.Add(foldedBoundary);
                        }
                    }
                }
            }
        }
        foreach (IVisitableAstNode child in IrAstChildren.ChildrenOf(node)) {
            ScanBlock(child, occurrences, folded, removed);
        }
    }
}
