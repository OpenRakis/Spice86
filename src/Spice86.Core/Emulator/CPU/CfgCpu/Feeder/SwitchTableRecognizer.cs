namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

using System;

/// <summary>
/// Recognizes the compiler-generated "bounds check + scale + indirect transfer" sequence in front
/// of a switch/call table read, e.g. <c>cmp ax,N; ja def; xchg bx,ax; shl bx,1; jmp [cs:bx+T]</c>.
/// Walks backwards at most sixteen instructions from the transfer through the transfer's block and
/// single-predecessor blocks, tracking the index register as <c>R = v*k + c</c> of the guarded
/// value v. Accepted steps: <c>mov</c>/<c>xchg</c> between registers, <c>shl R,1..3</c>,
/// <c>add R,R</c>, <c>add R,imm</c>, <c>xor Rh,Rh</c> and <c>cbw</c> widening, a byte load into the
/// low half, and stack instructions that do not write the tracked register. The bound is a
/// <c>cmp v,N</c> under a <c>ja</c>/<c>jae</c>/<c>jb</c>/<c>jbe</c> polarity check - each edge that
/// continues toward the transfer bounds the value from above or below, and several guards
/// combine into an inclusive <c>[low, high]</c> range - or <c>and v,2^n-1</c>. Any other
/// write to the tracked register refuses; a table is never accepted without a found bound. When
/// the compared value is already scaled (<c>shl si,1; cmp si,6</c>) the walk keeps going so the
/// scale lands in <see cref="TableShape.Step"/> and only reachable indices are read.
/// Recognition results, including negative ones, are cached per transfer node.
/// </summary>
internal class SwitchTableRecognizer {
    /// <summary>Maximum number of instructions walked backwards from the transfer.</summary>
    private const int MaxWalkBack = 16;

    /// <summary>
    /// A recognized table site. Entry <c>i</c> (<c>0..Bound</c>) is the word at
    /// <c>segment:BaseOffset + i*Step</c>.
    /// </summary>
    /// <param name="Bound">Largest valid index: the table has Bound+1 entries.</param>
    /// <param name="SegmentRegisterIndex">Index into <c>State.SegmentRegisters.UInt16</c> of the table segment.</param>
    /// <param name="BaseOffset">Displacement of the first entry inside the segment.</param>
    /// <param name="Step">Byte stride between consecutive entries (the accumulated index scale).</param>
    /// <param name="IsCall">True when the transfer is an indirect near call, false for a jump.</param>
    internal sealed record TableShape(ushort Bound, int SegmentRegisterIndex, ushort BaseOffset, ushort Step, bool IsCall);

    private readonly Dictionary<CfgInstruction, TableShape?> _cache = new();

    /// <summary>
    /// Recognizes the table shape ending at an observed <paramref name="transfer"/>, or null when
    /// the walk does not match. Negative results are cached like positive ones.
    /// </summary>
    public TableShape? Recognize(CfgInstruction transfer) {
        if (_cache.TryGetValue(transfer, out TableShape? cached)) {
            return cached;
        }
        TableShape? result = IsIndirectTransfer(transfer) ? TryRecognize(transfer, speculative: false) : null;
        _cache[transfer] = result;
        return result;
    }

    /// <summary>
    /// Recognizes the table shape ending at a speculative <paramref name="transfer"/>: same
    /// backward walk, but predecessor steps may go through speculative nodes too.
    /// </summary>
    internal static TableShape? RecognizeSpeculative(CfgInstruction transfer) =>
        TryRecognize(transfer, speculative: true);

    /// <summary>
    /// True when <paramref name="node"/> is a live near indirect jmp/call reading a word memory
    /// operand: the only kind of instruction a table resolution can apply to. Cheap enough to run
    /// per execution as a first filter before the cached <see cref="Recognize"/>.
    /// </summary>
    internal static bool IsIndirectTransfer(ICfgNode node) =>
        node is CfgInstruction { IsLive: true, DisplayAst: InstructionNode {
            Operation: InstructionOperation.JMP_NEAR or InstructionOperation.CALL_NEAR,
            Parameters: [SegmentedPointerNode { DataType.BitWidth: BitWidth.WORD_16 }] } };

    private static TableShape? TryRecognize(CfgInstruction transfer, bool speculative) {
        if (transfer.DisplayAst is not InstructionNode {
                Parameters: [SegmentedPointerNode pointer] } display
            || display.Operation is not (InstructionOperation.JMP_NEAR or InstructionOperation.CALL_NEAR)
            || pointer.DataType.BitWidth is not BitWidth.WORD_16) {
            return null;
        }
        if (!TryGetIndexExpression(pointer.Offset, out int indexRegister, out ushort displacement)
            || GetSegmentRegisterIndex(pointer) is not int segmentRegisterIndex) {
            return null;
        }

        // The tracked value W, expressed as R = k*W + c, starts as the index register itself.
        // Walking backwards: a mov/xchg re-points W at another register, a scale folds into k and
        // c. Bounds found by a cmp (polarity-checked) or an and mask are kept per register slot so
        // a compare on a register that only becomes the tracked one further up the chain still
        // bounds the table. Any other write to the tracked register refuses.
        int tracked = indexRegister;
        bool byteWidth = false;
        int scale = 1;
        int bias = 0;
        Dictionary<int, IndexBound> pendingBounds = new();
        CfgInstruction? lastJump = null;
        GuardCondition? lastJumpCondition = null;
        CfgInstruction jumpAfter = transfer;
        CfgInstruction afterJump = transfer;
        List<CfgInstruction> chain = WalkBack(transfer, speculative);
        bool cbwWidened = false;
        for (int chainIndex = 0; chainIndex < chain.Count; chainIndex++) {
            CfgInstruction node = chain[chainIndex];
            // The conditional jump the guard feeds: the nearest conditional jump met going
            // backwards is the first one after the guard in execution order. The instruction
            // right after it is the element iterated just before it.
            CfgInstruction previousForward = afterJump;
            afterJump = node;
            if (TryMatchConditionalJump(node, out GuardCondition condition)) {
                lastJump = node;
                lastJumpCondition = condition;
                jumpAfter = previousForward;
            } else if (IsUnknownConditionalJump(node)) {
                // A conditional jump of a shape other than ja/jae/jb/jbe between the guard and
                // the transfer makes the bound's polarity unverifiable.
                lastJump = node;
                lastJumpCondition = null;
                jumpAfter = previousForward;
            }
            if (TryMatchGuard(node, out int compared, out bool comparedByte, out ushort immediate)) {
                if (lastJump is null || lastJumpCondition is not GuardCondition jumpCondition
                    || !GuardRange(lastJump, jumpCondition, jumpAfter, immediate,
                        out IndexBound range)) {
                    return null;
                }
                MergeBound(pendingBounds, BoundKey(compared, comparedByte), range);
                continue;
            }
            if (TryMatchAnd(node, out int masked, out bool maskedByte, out ushort mask)) {
                MergeBound(pendingBounds, BoundKey(masked, maskedByte), new IndexBound(null, mask));
                continue;
            }
            if (PushedAddressMatcher.TouchesStack(node)) {
                if (!AcceptStackStep(node, chain, chainIndex, tracked, byteWidth)
                    && !pendingBounds.ContainsKey(BoundKey(tracked, byteWidth))) {
                    return null;
                }
                continue;
            }
            ushort writes = RegisterWriteSet.MayWrite(node);
            if ((writes & (1 << TrackedParent(tracked, byteWidth))) == 0) {
                // A write to another register erases its recorded bound: it cannot be related to
                // the value the bound describes.
                ClearBounds(pendingBounds, writes);
                continue;
            }
            if (!ApplyTransform(node, pendingBounds, ref tracked, ref byteWidth, ref scale, ref bias,
                    ref cbwWidened)) {
                // An unhandled write to the tracked register ends the walk: when a bound on the
                // value at the compare was already found it still enumerates every index the
                // transfer can take; without one there is no bound at all.
                if (!pendingBounds.ContainsKey(BoundKey(tracked, byteWidth))) {
                    return null;
                }
                break;
            }
        }
        if (!pendingBounds.TryGetValue(BoundKey(tracked, byteWidth), out IndexBound bound)
            || bound.Hi is not long high || high > ushort.MaxValue
            || (cbwWidened && byteWidth && high > 0x7F)) {
            return null;
        }
        long low = bound.Lo ?? 0;
        if (low < 0) {
            low = 0;
        }
        if (low > high) {
            return null;
        }
        // The first entry read is the one at index low: fold it into the base offset so the
        // shape keeps enumerating Bound+1 words from BaseOffset by Step.
        return new TableShape((ushort)(high - low), segmentRegisterIndex,
            (ushort)(displacement + bias + low * scale), (ushort)scale,
            display.Operation == InstructionOperation.CALL_NEAR);
    }

    /// <summary>An inclusive bound on the guarded value: <c>Lo</c> is the smallest valid index
    /// (null means unbounded below), <c>Hi</c> the largest (null means unbounded above).</summary>
    private readonly record struct IndexBound(long? Lo, long? Hi);

    private static void MergeBound(Dictionary<int, IndexBound> pendingBounds, int key,
        IndexBound bound) {
        if (!pendingBounds.TryGetValue(key, out IndexBound existing)) {
            pendingBounds[key] = bound;
            return;
        }
        pendingBounds[key] = new IndexBound(
            Max(existing.Lo, bound.Lo), Min(existing.Hi, bound.Hi));
    }

    private static long? Max(long? a, long? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static long? Min(long? a, long? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    private static int BoundKey(int registerIndex, bool byteWidth) => registerIndex | (byteWidth ? 16 : 0);

    private static int TrackedParent(int tracked, bool byteWidth) =>
        byteWidth && tracked > 3 ? tracked - 4 : tracked;

    private static void ClearBounds(Dictionary<int, IndexBound> pendingBounds, ushort writes) {
        for (int bit = 0; bit < 8; bit++) {
            if ((writes & (1 << bit)) != 0) {
                pendingBounds.Remove(bit);
                pendingBounds.Remove(bit | 16);
            }
        }
    }

    /// <summary>The condition a guard conditional jump tests on the compared value.</summary>
    private enum GuardCondition { Above, AboveOrEqual, Below, BelowOrEqual }

    /// <summary>Derives the index range that reaches the transfer past a compare/jump guard: the
    /// edge that continues toward the transfer decides whether the jump condition bounds the
    /// value from above (taken edge reaches: <c>jb</c>/<c>jbe</c>, fallthrough reaches:
    /// <c>ja</c>/<c>jae</c>) or from below (the complement). False when neither or both edges
    /// reach, i.e. the jump does not guard the transfer.</summary>
    private static bool GuardRange(CfgInstruction jump, GuardCondition condition,
        CfgInstruction afterJump, int compared, out IndexBound range) {
        SegmentedAddress after = afterJump.Address;
        bool fallthroughReaches = jump.NextInMemoryAddress32.ToSegmentedAddress().Equals(after);
        bool takenReaches = jump.StaticSuccessorAddresses.Count > 0
            && jump.StaticSuccessorAddresses[0].Equals(after);
        range = default;
        if (takenReaches == fallthroughReaches) {
            return false;
        }
        range = condition switch {
            // taken: v > N - fallthrough: v <= N
            GuardCondition.Above => takenReaches
                ? new IndexBound(compared + 1L, null) : new IndexBound(null, compared),
            // taken: v >= N - fallthrough: v < N
            GuardCondition.AboveOrEqual => takenReaches
                ? new IndexBound(compared, null) : new IndexBound(null, compared - 1L),
            // taken: v < N - fallthrough: v >= N
            GuardCondition.Below => takenReaches
                ? new IndexBound(null, compared - 1L) : new IndexBound(compared, null),
            // taken: v <= N - fallthrough: v > N
            _ => takenReaches
                ? new IndexBound(null, compared) : new IndexBound(compared + 1L, null),
        };
        return true;
    }

    /// <summary>A stack instruction is transparent when it cannot write the tracked register.
    /// A push additionally has to be the manufactured-return shape (imm16, or r16 resolved to a
    /// <c>mov r16,imm16</c> setter) and never pushes the tracked register itself.</summary>
    private static bool AcceptStackStep(CfgInstruction node, List<CfgInstruction> chain,
        int chainIndex, int tracked, bool byteWidth) {
        if (node.DisplayAst is InstructionNode { Operation: InstructionOperation.PUSH }) {
            int trackedParent = TrackedParent(tracked, byteWidth);
            if (!PushedAddressMatcher.TryMatchPush(node, out int pushedRegister, out ushort _)
                || pushedRegister == trackedParent) {
                return false;
            }
            if (pushedRegister < 0) {
                return true;
            }
            List<CfgInstruction> remaining = chain.GetRange(chainIndex + 1, chain.Count - chainIndex - 1);
            return PushedAddressMatcher.ResolvePushedImmediate(pushedRegister, remaining) is not null;
        }
        return !IsStackWriteToTracked(node, tracked, byteWidth);
    }

    /// <summary>Applies one accepted transformation to the tracked register and carries a bound
    /// recorded on it through the transform. Returns false when the instruction writes the
    /// tracked register in a way that is not an accepted step.</summary>
    private static bool ApplyTransform(CfgInstruction node, Dictionary<int, IndexBound> pendingBounds,
        ref int tracked, ref bool byteWidth, ref int scale, ref int bias, ref bool cbwWidened) {
        if (node.DisplayAst is not InstructionNode { Parameters: var parameters } display) {
            return false;
        }
        switch (display.Operation) {
            case InstructionOperation.MOV or InstructionOperation.XCHG
                when parameters.Length == 2: {
                    RegisterNode? left = AsRegister(parameters[0]);
                    RegisterNode? right = AsRegister(parameters[1]);
                    // mov Wh,0 zeroes the high byte like xor Wh,Wh: the tracked value narrows
                    // to the low byte.
                    if (display.Operation == InstructionOperation.MOV && !byteWidth && tracked <= 3
                        && left is { DataType.BitWidth: BitWidth.BYTE_8 }
                        && left.RegisterIndex == tracked + 4
                        && AsConstant(parameters[1]) is 0) {
                        byteWidth = true;
                        MoveBound(pendingBounds, BoundKey(tracked, false), BoundKey(tracked, true));
                        return true;
                    }
                    if (left is null || left.DataType.BitWidth != Width(byteWidth)) {
                        return false;
                    }
                    if (display.Operation == InstructionOperation.XCHG) {
                        if (right is null || right.DataType.BitWidth != Width(byteWidth)) {
                            return false;
                        }
                        if (left.RegisterIndex == tracked) {
                            tracked = right.RegisterIndex;
                        } else if (right.RegisterIndex == tracked) {
                            tracked = left.RegisterIndex;
                        } else {
                            return false;
                        }
                        MoveBound(pendingBounds, BoundKey(left.RegisterIndex, byteWidth),
                            BoundKey(right.RegisterIndex, byteWidth));
                        return true;
                    }
                    if (left.RegisterIndex != tracked) {
                        return false;
                    }
                    // mov W, reg re-points the tracked value; mov W, [mem] keeps it in W. A
                    // constant source sets the index to a value the bound does not describe.
                    if (right is not null) {
                        if (right.DataType.BitWidth != Width(byteWidth)) {
                            return false;
                        }
                        tracked = right.RegisterIndex;
                        MoveBound(pendingBounds, BoundKey(left.RegisterIndex, byteWidth),
                            BoundKey(right.RegisterIndex, byteWidth));
                        return true;
                    }
                    return AsConstant(parameters[1]) is null;
                }
            case InstructionOperation.SHL
                when parameters.Length == 2
                     && AsRegister(parameters[0]) is { DataType.BitWidth: var w, RegisterIndex: var i }
                     && w == Width(byteWidth) && i == tracked
                     && AsConstant(parameters[1]) is ulong shift && shift is >= 1 and <= 3:
                scale <<= (int)shift;
                AdjustBound(pendingBounds, BoundKey(tracked, byteWidth),
                    bound => ScaleBound(bound, 1 << (int)shift));
                return true;
            case InstructionOperation.ADD
                when parameters.Length == 2
                     && AsRegister(parameters[0]) is { DataType.BitWidth: var w, RegisterIndex: var i }
                     && w == Width(byteWidth) && i == tracked: {
                    if (AsRegister(parameters[1]) is { RegisterIndex: var other }
                        && other == tracked) {
                        scale <<= 1;
                        AdjustBound(pendingBounds, BoundKey(tracked, byteWidth),
                            bound => ScaleBound(bound, 2));
                        return true;
                    }
                    if (AsConstant(parameters[1]) is ulong immediate) {
                        bias += scale * (int)immediate;
                        AdjustBound(pendingBounds, BoundKey(tracked, byteWidth),
                            bound => new IndexBound(bound.Lo - (long)immediate,
                                bound.Hi - (long)immediate));
                        return true;
                    }
                    return false;
                }
            case InstructionOperation.XOR
                when parameters.Length == 2 && !byteWidth && tracked <= 3
                     && AsRegister(parameters[0]) is { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: var a }
                     && AsRegister(parameters[1]) is { DataType.BitWidth: BitWidth.BYTE_8, RegisterIndex: var b }
                     && a == tracked + 4 && b == tracked + 4:
                // xor Wh,Wh widens the low byte: the tracked value narrows to it.
                byteWidth = true;
                MoveBound(pendingBounds, BoundKey(tracked, false), BoundKey(tracked, true));
                return true;
            case InstructionOperation.CBW or InstructionOperation.CBWE
                when !byteWidth && tracked == 0:
                byteWidth = true;
                cbwWidened = true;
                MoveBound(pendingBounds, BoundKey(tracked, false), BoundKey(tracked, true));
                return true;
            default:
                return false;
        }
    }

    private static void MoveBound(Dictionary<int, IndexBound> pendingBounds, int from, int to) {
        if (pendingBounds.Remove(from, out IndexBound bound)) {
            MergeBound(pendingBounds, to, bound);
        }
    }

    private static void AdjustBound(Dictionary<int, IndexBound> pendingBounds, int key,
        Func<IndexBound, IndexBound> adjust) {
        if (pendingBounds.TryGetValue(key, out IndexBound bound)) {
            pendingBounds[key] = adjust(bound);
        }
    }

    /// <summary>Range of the unscaled value when the scaled one stays inside
    /// <paramref name="bound"/>: <c>R = v*divisor</c> keeps <c>v</c> in
    /// <c>[ceil(Lo/divisor), floor(Hi/divisor)]</c>.</summary>
    private static IndexBound ScaleBound(IndexBound bound, long divisor) =>
        new(CeilDiv(bound.Lo, divisor), FloorDiv(bound.Hi, divisor));

    private static long? FloorDiv(long? value, long divisor) {
        if (value is not long a) {
            return null;
        }
        long q = a / divisor;
        if (a % divisor != 0 && (a < 0) != (divisor < 0)) {
            q--;
        }
        return q;
    }

    private static long? CeilDiv(long? value, long divisor) =>
        value is long a ? -FloorDiv(-a, divisor) : null;

    private static BitWidth Width(bool byteWidth) => byteWidth ? BitWidth.BYTE_8 : BitWidth.WORD_16;

    /// <summary>Whether a stack-touching instruction writes the tracked register itself:
    /// <c>pop W</c>, or any stack operation when W is SP, or <c>enter</c>/<c>leave</c> when W is
    /// SP or BP.</summary>
    private static bool IsStackWriteToTracked(CfgInstruction node, int tracked, bool byteWidth) {
        int trackedParent = TrackedParent(tracked, byteWidth);
        if (node.DisplayAst is not InstructionNode { Operation: var op, Parameters: var parameters }) {
            return true;
        }
        switch (op) {
            case InstructionOperation.POP:
                return trackedParent == 4
                    || parameters.Length > 0
                    && AsRegister(parameters[0]) is { RegisterIndex: var popped }
                    && popped == trackedParent;
            case InstructionOperation.ENTER or InstructionOperation.ENTERW
                or InstructionOperation.LEAVE or InstructionOperation.LEAVEW:
                return trackedParent is 4 or 5;
            default:
                return trackedParent == 4;
        }
    }

    // --- backward walk ---

    /// <summary>Instructions before <paramref name="transfer"/>, most recent first, capped at
    /// <see cref="MaxWalkBack"/>. Stops at the first instruction without a unique usable
    /// instruction predecessor (block join or ambiguous chain).</summary>
    private static List<CfgInstruction> WalkBack(CfgInstruction transfer, bool speculative) {
        List<CfgInstruction> chain = new();
        CfgInstruction cursor = transfer;
        while (chain.Count < MaxWalkBack) {
            CfgInstruction? previous = PreviousInstruction(cursor, speculative);
            if (previous is null) {
                break;
            }
            chain.Add(previous);
            cursor = previous;
        }
        return chain;
    }

    /// <summary>
    /// The unique usable instruction predecessor of <paramref name="node"/>: the previous
    /// instruction inside its block, or, at a block head, the last instruction of the single
    /// predecessor block. Null when there is none or several (ambiguous chain).
    /// </summary>
    private static CfgInstruction? PreviousInstruction(CfgInstruction node, bool speculative) {
        CfgInstruction? previous = null;
        foreach (ICfgNode predecessor in node.Predecessors) {
            if (predecessor is not CfgInstruction instruction
                || !(instruction.IsLive || (speculative && instruction.IsSpeculative))) {
                continue;
            }
            if (previous is not null) {
                return null;
            }
            previous = instruction;
        }
        return previous;
    }

    // --- shape matchers on display ASTs ---

    internal static RegisterNode? AsRegister(IVisitableAstNode node) =>
        node is TypeConversionNode conversion ? conversion.Value as RegisterNode : node as RegisterNode;

    internal static ulong? AsConstant(IVisitableAstNode node) => node switch {
        TypeConversionNode { Value: var inner } => AsConstant(inner),
        ConstantNode constant => constant.Value,
        InstructionFieldNode field => field.ConstantValue,
        _ => null
    };

    /// <summary>Extracts [R + disp] from the pointer offset: a 16-bit register optionally added to
    /// a constant displacement (disp may be 0).</summary>
    internal static bool TryGetIndexExpression(ValueNode offset, out int registerIndex, out ushort displacement) {
        registerIndex = 0;
        displacement = 0;
        ValueNode inner = offset is TypeConversionNode conversion ? conversion.Value : offset;
        if (inner is RegisterNode { DataType.BitWidth: BitWidth.WORD_16 } register) {
            registerIndex = register.RegisterIndex;
            return true;
        }
        if (inner is BinaryOperationNode { BinaryOperation: BinaryOperation.PLUS } add) {
            RegisterNode? left = AsRegister(add.Left);
            ulong? right = AsConstant(add.Right);
            if (left is null || right is null) {
                left = AsRegister(add.Right);
                right = AsConstant(add.Left);
            }
            if (left is { DataType.BitWidth: BitWidth.WORD_16 } && right is not null) {
                registerIndex = left.RegisterIndex;
                displacement = (ushort)right.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>The segment register index of an explicit override segment, else the default
    /// segment. Null when neither is a segment register node.</summary>
    internal static int? GetSegmentRegisterIndex(SegmentedPointerNode pointer) {
        ValueNode? segment = pointer.Segment ?? pointer.DefaultSegment;
        return segment is SegmentRegisterNode node ? node.RegisterIndex : null;
    }

    /// <summary>The conditional jump the guard feeds: <c>ja</c>/<c>jae</c>/<c>jb</c>/<c>jbe</c>
    /// can bound a table once <see cref="GuardRange"/> has checked which edge continues toward
    /// the transfer.</summary>
    private static bool TryMatchConditionalJump(CfgInstruction node, out GuardCondition condition) {
        condition = default;
        if (node.DisplayAst is not InstructionNode { Operation: var op }) {
            return false;
        }
        switch (op) {
            case InstructionOperation.JA_SHORT or InstructionOperation.JA_NEAR:
                condition = GuardCondition.Above;
                return true;
            case InstructionOperation.JAE_SHORT or InstructionOperation.JAE_NEAR:
                condition = GuardCondition.AboveOrEqual;
                return true;
            case InstructionOperation.JB_SHORT or InstructionOperation.JB_NEAR:
                condition = GuardCondition.Below;
                return true;
            case InstructionOperation.JBE_SHORT or InstructionOperation.JBE_NEAR:
                condition = GuardCondition.BelowOrEqual;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Any other conditional jump: its presence between the compare and the transfer
    /// makes the bound's polarity unverifiable.</summary>
    private static bool IsUnknownConditionalJump(CfgInstruction node) =>
        node.IsJump && node.DisplayAst is InstructionNode { Operation: var op }
        && op.ToString().StartsWith('J')
        && op is not (InstructionOperation.JMP_NEAR or InstructionOperation.JMP_SHORT
            or InstructionOperation.JMP_FAR);

    /// <summary>guard compare: <c>cmp W, imm</c> on a register; the bound is recorded per register
    /// slot and applied if that slot is or becomes the tracked one.</summary>
    private static bool TryMatchGuard(CfgInstruction node, out int register, out bool byteWidth,
        out ushort bound) {
        register = 0;
        byteWidth = false;
        bound = 0;
        if (node.DisplayAst is not InstructionNode {
                Operation: InstructionOperation.CMP,
                Parameters: [var target, var source] }
            || AsConstant(source) is not ulong immediate
            || AsRegister(target) is not { RegisterIndex: var index, DataType.BitWidth: var width }
            || width is not (BitWidth.BYTE_8 or BitWidth.WORD_16)) {
            return false;
        }
        register = index;
        byteWidth = width is BitWidth.BYTE_8;
        bound = (ushort)immediate;
        return true;
    }

    /// <summary>bound by mask: <c>and W, 2^n-1</c> on a register.</summary>
    private static bool TryMatchAnd(CfgInstruction node, out int register, out bool byteWidth,
        out ushort mask) {
        register = 0;
        byteWidth = false;
        mask = 0;
        if (node.DisplayAst is not InstructionNode {
                Operation: InstructionOperation.AND,
                Parameters: [var target, var source] }
            || AsConstant(source) is not ulong immediate
            || (immediate & (immediate + 1)) != 0
            || AsRegister(target) is not { RegisterIndex: var index, DataType.BitWidth: var width }
            || width is not (BitWidth.BYTE_8 or BitWidth.WORD_16)) {
            return false;
        }
        register = index;
        byteWidth = width is BitWidth.BYTE_8;
        mask = (ushort)immediate;
        return true;
    }
}
