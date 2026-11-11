namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Recognizes a register-pair operation spread over two or three consecutive instructions whose
/// halves are linked by the carry (<c>add</c>/<c>adc</c>, <c>sub</c>/<c>sbb</c>,
/// <c>neg</c>/<c>adc</c>/<c>neg</c>, <c>shl</c>/<c>rcl</c>, <c>shr</c>/<c>rcr</c>) and rewrites it as
/// one operation on the combined value: a wide temporary declaration followed by the two half
/// assignments. The pass runs on the folded shapes of the late pipeline (the alu calls already
/// expanded and propagated); methods without a pair are left untouched.
/// A pair's two halves print joined when they already are one value: the byte registers of one
/// 16-bit register become that register, and two half writes of one register become one wide
/// assignment.
/// </summary>
internal static class IrDoublePrecision {
    private static readonly RegisterRenderer Registers = new(AsmRenderingConfig.CreateSpice86Style());

    /// <summary>Rewrites every register-pair operation of <paramref name="method"/>; returns how many.</summary>
    public static int Apply(IrMethod method, IrSsa ssa) {
        // Counted loops are planned first so later SSA queries still see the original method:
        // their body is the pair shift the per-block matcher would otherwise consume before the
        // loop shape can be proven.
        List<CountedLoopPlan> loopPlans = CountedPairShiftLoops(method, ssa);
        HashSet<IrBlock> loopBlocks = [.. loopPlans.Select(plan => plan.Block)];
        HashSet<ICfgNode> pinnedOrigins = IrInstructionIdioms.PinnedOriginsOf(method);
        List<(IrBlock Block, List<Match> Matches)> found = [];
        foreach (IrBlock block in method.Blocks) {
            if (loopBlocks.Contains(block)) {
                continue;
            }
            List<Run> runs = RunsOf(block);
            List<Match> matches = [];
            List<(int From, int To)> claimed = [];
            for (int r = 0; r < runs.Count; r++) {
                if (MatchAt(block, runs, r, ssa) is { } match) {
                    // A non-adjacent match may span another match's window: rewritten indices
                    // would shift under it, so overlapping or nested matches are dropped.
                    int from = match.Indices[0];
                    int to = match.Indices[^1];
                    if (claimed.Any(window => from <= window.To && to >= window.From)
                        || IrInstructionIdioms.CrossesPinnedOrigin(block.Statements, from, to,
                            pinnedOrigins)) {
                        continue;
                    }
                    matches.Add(match);
                    claimed.Add((from, to));
                    r += match.Runs - 1;
                }
            }
            if (matches.Count > 0) {
                found.Add((block, matches));
            }
        }
        int rewritten = loopPlans.Count;
        foreach (CountedLoopPlan plan in loopPlans) {
            plan.Block.Statements.Clear();
            plan.Block.Statements.AddRange(plan.Statements);
            plan.Block.Terminator = plan.Terminator;
        }
        if (found.Count == 0) {
            return rewritten;
        }
        foreach ((IrBlock block, List<Match> matches) in found) {
            // Rewrite from the last pair to the first: the positions were collected on the original
            // statement list and an earlier rewrite shifts every later one.
            for (int i = matches.Count - 1; i >= 0; i--) {
                Rewrite(method, block, matches[i]);
            }
            rewritten += matches.Count;
        }
        return rewritten;
    }

    /// <summary>
    /// The wide value <paramref name="form"/> computes on the pair: <paramref name="alo"/>/
    /// <paramref name="ahi"/> are the low and high halves of the left operand (for the shift and
    /// negate forms, the low and high registers themselves) and <paramref name="blo"/>/
    /// <paramref name="bhi"/> the halves of the right operand (add and subtract only). Unused
    /// operands are ignored.
    /// </summary>
    internal static ValueNode Combined(DoublePrecisionForm form, DataType half,
        ValueNode alo, ValueNode ahi, ValueNode blo, ValueNode bhi) {
        DataType wide = WideType(half);
        int width = Width(half);
        return form switch {
            DoublePrecisionForm.Add => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.PLUS,
                Pair(wide, blo, bhi, width)),
            DoublePrecisionForm.Subtract => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.MINUS,
                Pair(wide, blo, bhi, width)),
            DoublePrecisionForm.Negate => new BinaryOperationNode(wide,
                new ConstantNode(wide, 0), BinaryOperation.MINUS,
                Pair(wide, alo, ahi, width)),
            DoublePrecisionForm.ShiftLeft => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.LEFT_SHIFT,
                new ConstantNode(DataType.INT32, 1)),
            DoublePrecisionForm.ShiftRight => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, 1)),
            DoublePrecisionForm.SignedShiftRight => new TypeConversionNode(wide,
                new BinaryOperationNode(SignedWideType(half),
                    new TypeConversionNode(SignedWideType(half), Pair(wide, alo, ahi, width)),
                    BinaryOperation.RIGHT_SHIFT, new ConstantNode(DataType.INT32, 1))),
            DoublePrecisionForm.And => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.BITWISE_AND,
                Pair(wide, blo, bhi, width)),
            DoublePrecisionForm.Or => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.BITWISE_OR,
                Pair(wide, blo, bhi, width)),
            DoublePrecisionForm.Xor => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.BITWISE_XOR,
                Pair(wide, blo, bhi, width)),
            DoublePrecisionForm.Not => new UnaryOperationNode(wide,
                UnaryOperation.BITWISE_NOT, Pair(wide, alo, ahi, width)),
            _ => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.MULTIPLY,
                Pair(wide, blo, bhi, width))
        };
    }

    /// <summary>The <c>(T)(half)lo | (T)(half)hi &lt;&lt; width</c> tree of one pair operand,
    /// or the 16-bit register when the two halves are its byte registers. Each half is first
    /// truncated to its unsigned half width: a wider expression cannot leak bits above it into
    /// the high half and a signed half cannot sign-extend.</summary>
    internal static ValueNode Pair(DataType wide, ValueNode lo, ValueNode hi, int width) {
        if (lo is RegisterNode lowRegister && hi is RegisterNode highRegister
            && ParentOf(lowRegister, highRegister) is { } parent) {
            return parent;
        }
        DataType halfType = DataType.UnsignedFromBitWidth((BitWidth)width);
        return new BinaryOperationNode(wide,
            new TypeConversionNode(wide, new TypeConversionNode(halfType, lo)),
            BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(wide,
                new TypeConversionNode(wide, new TypeConversionNode(halfType, hi)),
                BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, (ulong)width)));
    }

    /// <summary><paramref name="node"/> as <c>(wide)(half)x</c>: the operand under the zero
    /// extension, or null. The outer conversion must reach <paramref name="wide"/> from a value of
    /// exactly the unsigned <paramref name="half"/> type - a direct signed-half widening is sign
    /// extension, not zero extension. Inner layers then peel only while both sides keep the half
    /// width: a signedness round-trip cannot change the value, a width-changing cast could.</summary>
    internal static ValueNode? ZeroExtendedHalf(ValueNode node, DataType half, DataType wide) {
        if (node is not TypeConversionNode { DataType: { } outer, Value: { } value }
            || outer != wide || value.DataType != half || half.Signed) {
            return null;
        }
        while (value is TypeConversionNode inner
                && inner.DataType.BitWidth == half.BitWidth
                && inner.Value.DataType.BitWidth == half.BitWidth) {
            value = inner.Value;
        }
        return value;
    }

    /// <summary>The 16-bit register <paramref name="low"/> and <paramref name="high"/> are the two
    /// byte halves of, or <see langword="null"/>: the x86 register index puts the low bytes AL..BL at
    /// 0-3 and the high bytes AH..BH at 4-7 over the same word index (<see cref="IrValueSets"/>).</summary>
    private static RegisterNode? ParentOf(RegisterNode low, RegisterNode high) =>
        low.DataType.BitWidth == BitWidth.BYTE_8 && high.DataType.BitWidth == BitWidth.BYTE_8
            && low.RegisterIndex + 4 == high.RegisterIndex
            ? new RegisterNode(DataType.UINT16, low.RegisterIndex) : null;

    private static DataType WideType(DataType half) => half.BitWidth switch {
        BitWidth.BYTE_8 => DataType.UINT16,
        BitWidth.WORD_16 => DataType.UINT32,
        BitWidth.DWORD_32 => DataType.UINT64,
        _ => throw new ArgumentException($"No pair type for {half}")
    };

    private static DataType SignedWideType(DataType half) =>
        DataType.SignedFromBitWidth(WideType(half).BitWidth);

    private static int Width(DataType half) => (int)half.BitWidth;

    /// <summary>A matched pattern: its statements' positions, the registers and operand trees it
    /// binds and the <see cref="DoublePrecisionForm"/> it rewrites as.</summary>
    private sealed record Match(DoublePrecisionForm Form, DataType Half, RegisterNode Low,
        RegisterNode High, ValueNode ALo, ValueNode AHi, ValueNode? BLo, ValueNode? BHi,
        List<int> Indices, ICfgNode FirstOrigin, ICfgNode LastOrigin, int Runs,
        bool OmitFirstHalfWrite);

    /// <summary>The statements of one instruction of the block: the indices of the statements
    /// that are not the instruction's boundary marker.</summary>
    private sealed record Run(ICfgNode Origin, List<int> BodyIndices);

    private static Match? MatchAt(IrBlock block, List<Run> runs, int r, IrSsa ssa) {
        if (TryNegate(block, runs, r) is { } match) {
            return match;
        }
        if (TryShift(block, runs, r, true) is { } leftShift) {
            return leftShift;
        }
        if (TryShift(block, runs, r, false) is { } rightShift) {
            return rightShift;
        }
        if (TryAddSub(block, runs, r, true) is { } add) {
            return add;
        }
        if (TryAddSub(block, runs, r, false) is { } subtract) {
            return subtract;
        }
        return TryLogical(block, runs, r, ssa);
    }

    private static List<Run> RunsOf(IrBlock block) {
        List<Run> runs = [];
        for (int i = 0; i < block.Statements.Count; i++) {
            ICfgNode origin = block.Statements[i].Origin;
            if (runs.Count > 0 && runs[^1].Origin.Equals(origin)) {
                if (block.Statements[i].Node is not InstructionStartNode) {
                    runs[^1].BodyIndices.Add(i);
                }
                continue;
            }
            List<int> body = [];
            if (block.Statements[i].Node is not InstructionStartNode) {
                body.Add(i);
            }
            runs.Add(new Run(origin, body));
        }
        return runs;
    }

    /// <summary><paramref name="run"/> minus a leading prefix of event checks, speculative
    /// guards and comments: those statements stay before the combined operation, exactly where
    /// they observed the loop or the entry point. Only a pattern's first run is trimmed - the
    /// same statement between the two halves still refuses.</summary>
    private static Run FirstBodyRun(Run run, IrBlock block) {
        int skip = 0;
        while (skip < run.BodyIndices.Count
            && block.Statements[run.BodyIndices[skip]].Node
                is CheckExternalEventsNode or SpeculativeGuardNode or CommentNode) {
            skip++;
        }
        return skip == 0 ? run : new Run(run.Origin, [.. run.BodyIndices.Skip(skip)]);
    }

    /// <summary>Whether every statement the pattern would consume or replace is side-effect free:
    /// no fault, no delivered interrupt and no token write (memory, IO).</summary>
    private static bool Safe(Run run, IrBlock block) =>
        run.BodyIndices.All(index => {
            IrEffects effects = block.Statements[index].Effects;
            return !effects.MayFault && !effects.DeliversInterrupts
                && effects.TokenWrites == IrTokens.None;
        });

    /// <summary>Whether <paramref name="low"/> and <paramref name="high"/> are two general registers
    /// of the same 8-, 16- or 32-bit type covering disjoint bytes.</summary>
    private static bool LegalHalves(RegisterNode? low, RegisterNode? high) {
        if (low is null or SegmentRegisterNode || high is null or SegmentRegisterNode
            || !low.DataType.Equals(high.DataType)
            || low.DataType.BitWidth is not (BitWidth.BYTE_8 or BitWidth.WORD_16
                or BitWidth.DWORD_32)
            || (IrValueSets.Of(low) & IrValueSets.Of(high)) != CpuValues.None) {
            return false;
        }
        return true;
    }

    /// <summary>Whether no byte of <paramref name="register"/> is read by <paramref name="node"/>.</summary>
    private static bool DoesNotRead(ValueNode node, RegisterNode register) =>
        (IrEffectsVisitor.Of(node, false).Reads & IrValueSets.Of(register)) == CpuValues.None;

    private static Match? TryNegate(IrBlock block, List<Run> runs, int r) {
        if (r + 2 >= runs.Count) {
            return null;
        }
        (Run first, Run second, Run third) = (FirstBodyRun(runs[r], block), runs[r + 1],
            runs[r + 2]);
        if (first.BodyIndices.Count != 2 || second.BodyIndices.Count != 1
            || third.BodyIndices.Count != 1) {
            return null;
        }
        if (Assign(block.Statements[first.BodyIndices[0]].Node) is not
                (RegisterNode low, { } negate)
            || ZeroMinusOperand(negate) is not { } alo
            || Assign(block.Statements[first.BodyIndices[1]].Node) is not
                (CpuFlagNode carryFlag, { } carryRelation)
            || !IsCarryOfResult(carryRelation, low)) {
            return null;
        }
        ushort mask = carryFlag.FlagMask;
        if (Assign(block.Statements[second.BodyIndices[0]].Node) is not
                (RegisterNode high, { } increment)
            || Op(increment, BinaryOperation.PLUS) is not { } plus
            || !plus.Left.Equals(high) || !IsCarryConversion(plus.Right, mask, high.DataType)
            || Assign(block.Statements[third.BodyIndices[0]].Node) is not
                (RegisterNode highAgain, { } negateAgain)
            || !highAgain.Equals(high) || ZeroMinusOperand(negateAgain) is not { } negated
            || !negated.Equals(high)) {
            return null;
        }
        if (!LegalHalves(low, high) || !Safe(first, block) || !Safe(second, block)
            || !Safe(third, block)) {
            return null;
        }
        List<int> indices = [.. first.BodyIndices, .. second.BodyIndices, .. third.BodyIndices];
        return new Match(DoublePrecisionForm.Negate, low.DataType, low, high, alo, high, null, null,
            indices, first.Origin, third.Origin, 3, false);
    }

    private static Match? TryShift(IrBlock block, List<Run> runs, int r, bool left) {
        if (r + 1 >= runs.Count) {
            return null;
        }
        (Run first, Run second) = (FirstBodyRun(runs[r], block), runs[r + 1]);
        if (second.BodyIndices.Count != 1
            || first.BodyIndices.Count is not (1 or 2)) {
            return null;
        }
        // shl: first instruction computes the carry out of the low half's sign bit and shifts it;
        // shr: the same on the high half's low bit. The shift write itself may already be gone when
        // the shifted value was forwarded to its single reader and the register write was dead.
        BinaryOperation shift = left ? BinaryOperation.LEFT_SHIFT : BinaryOperation.RIGHT_SHIFT;
        if (ShiftCarrySide(first, block, shift, left) is not { } carry
            || ShiftRotateSide(second, block, shift, left, carry.Mask, carry.ShiftedHalf)
                is not { } other) {
            return null;
        }
        RegisterNode low = left ? carry.ShiftedHalf : other;
        RegisterNode high = left ? other : carry.ShiftedHalf;
        if (!LegalHalves(low, high) || !Safe(first, block) || !Safe(second, block)) {
            return null;
        }
        List<int> indices = [.. first.BodyIndices, .. second.BodyIndices];
        DoublePrecisionForm form = left ? DoublePrecisionForm.ShiftLeft
            : carry.Signed ? DoublePrecisionForm.SignedShiftRight : DoublePrecisionForm.ShiftRight;
        return new Match(form, low.DataType, low, high, low, high, null, null, indices,
            first.Origin, second.Origin, 2, first.BodyIndices.Count == 1);
    }

    /// <summary>Validates the carry-out side of the pair: the flag carry assignment whose masked
    /// relation tests the shifted half's boundary bit, plus its optional own shift statement.
    /// Returns the shifted register (null when the shift write was already dead), the shifted
    /// half register, the carry flag mask and whether the shift is arithmetic (a signed
    /// same-width cast on its operand). A dead first-half write leaves no signedness evidence:
    /// the rotate does not read it, so the unsigned form is reported.</summary>
    private static (RegisterNode? Shifted, RegisterNode ShiftedHalf, ushort Mask, bool Signed)?
            ShiftCarrySide(Run first, IrBlock block, BinaryOperation shift, bool left) {
        if (Assign(block.Statements[first.BodyIndices[0]].Node) is not
                (CpuFlagNode carryFlag, { } carryRelation)) {
            return null;
        }
        RegisterNode? shifted;
        bool signed = false;
        if (first.BodyIndices.Count == 2) {
            if (Assign(block.Statements[first.BodyIndices[1]].Node) is not
                    (RegisterNode shiftedRegister, { } shiftedValue)
                || Op(UnwrapConversion(shiftedValue), shift) is not { } ownShift
                || !IsShiftCount(ownShift.Right, 1)) {
                return null;
            }
            // A SAR carries a signed same-width cast around its operand: (short)DX >> 1.
            if (ownShift.Left is TypeConversionNode { DataType: { Signed: true } castType,
                    Value: RegisterNode casted }
                    && castType == DataType.SignedFromBitWidth(shiftedRegister.DataType.BitWidth)
                    && casted.Equals(shiftedRegister)) {
                signed = true;
            } else if (!ownShift.Left.Equals(shiftedRegister)) {
                return null;
            }
            shifted = shiftedRegister;
        } else {
            shifted = null;
        }
        if (Op(carryRelation, BinaryOperation.NOT_EQUAL) is not { } relation
            || Op(relation.Left, BinaryOperation.BITWISE_AND) is not { } masked
            || masked.Left is not RegisterNode shiftedOperand
            || (shifted is not null && !shiftedOperand.Equals(shifted))
            || masked.Right is not ConstantNode { } bitConstant) {
            return null;
        }
        RegisterNode shiftedHalf = shifted ?? shiftedOperand;
        int width = Width(shiftedHalf.DataType);
        ulong testedBit = left ? 1UL << (width - 1) : 1UL;
        if (bitConstant.Value != testedBit
            || !IsZero(relation.Right, shiftedHalf.DataType)) {
            return null;
        }
        return (shifted, shiftedHalf, carryFlag.FlagMask, signed);
    }

    /// <summary>Validates the rotate-in side of the pair: the other half's assignment rotating the
    /// carry bit in. Returns the other half's register.</summary>
    private static RegisterNode? ShiftRotateSide(Run second, IrBlock block, BinaryOperation shift,
        bool left, ushort mask, RegisterNode shiftedHalf) {
        if (Assign(block.Statements[second.BodyIndices[0]].Node) is not
                (RegisterNode other, { } rotated)
            || Op(rotated, BinaryOperation.BITWISE_OR) is not { } or
            || Op(or.Left, shift) is not { } otherShift
            || !otherShift.Left.Equals(other) || !IsShiftCount(otherShift.Right, 1)) {
            return null;
        }
        if (left) {
            if (!IsCarryConversion(or.Right, mask, shiftedHalf.DataType)) {
                return null;
            }
        } else {
            if (Op(or.Right, BinaryOperation.LEFT_SHIFT) is not { } carryShift
                || !IsShiftCount(carryShift.Right, (ulong)(Width(shiftedHalf.DataType) - 1))
                || !IsCarryConversion(carryShift.Left, mask, shiftedHalf.DataType)) {
                return null;
            }
        }
        return other;
    }

    private static Match? TryAddSub(IrBlock block, List<Run> runs, int r, bool add) {
        if (r + 1 >= runs.Count) {
            return null;
        }
        (Run first, Run second) = (FirstBodyRun(runs[r], block), runs[r + 1]);
        if (first.BodyIndices.Count != 1 || second.BodyIndices.Count != 1) {
            return null;
        }
        BinaryOperation combine = add ? BinaryOperation.PLUS : BinaryOperation.MINUS;
        if (Assign(block.Statements[first.BodyIndices[0]].Node) is not
                (RegisterNode low, { } lowSum)
            || Op(UnwrapConversion(lowSum), combine) is not { } lowOperation) {
            return null;
        }
        ValueNode alo = lowOperation.Left;
        ValueNode blo = lowOperation.Right;
        if (Assign(block.Statements[second.BodyIndices[0]].Node) is not
                (RegisterNode high, { } highSum)
            || Op(UnwrapConversion(highSum), combine) is not { } highOperation) {
            return null;
        }
        // adc high,0 folds `high + 0` to the bare register; the carry addend then stands alone.
        ValueNode ahi;
        ValueNode bhi;
        if (Op(highOperation.Left, combine) is { } highHalves) {
            ahi = highHalves.Left;
            bhi = highHalves.Right;
        } else {
            ahi = highOperation.Left;
            bhi = new ConstantNode(low.DataType, 0);
        }
        if (highOperation.Right is TypeConversionNode { } carryConversion) {
            if (!carryConversion.DataType.Equals(low.DataType)) {
                return null;
            }
            if (Op(carryConversion.Value, BinaryOperation.LESS_THAN) is { } lessThan) {
                if (!RelationReads(lessThan, low, blo, add)) {
                    return null;
                }
            } else if (!IsZeroCarryRelation(carryConversion.Value, low, blo, add)) {
                return null;
            }
        } else {
            return null;
        }
        if (!LegalHalves(low, high)
            || !IrVerifier.IsPureArm(blo) || !IrVerifier.IsPureArm(bhi)
            || !DoesNotRead(ahi, low) || !DoesNotRead(bhi, low)
            || !Safe(first, block) || !Safe(second, block)) {
            return null;
        }
        List<int> indices = [.. first.BodyIndices, .. second.BodyIndices];
        return new Match(add ? DoublePrecisionForm.Add : DoublePrecisionForm.Subtract,
            low.DataType, low, high, alo, ahi, blo, bhi, indices,
            first.Origin, second.Origin, 2, false);
    }

    /// <summary>Whether <paramref name="relation"/> is the carry out of <c>low + 1</c>:
    /// <c>low + 1 &lt; 1</c> on the just-written result folds to <c>low == 0</c>, which the
    /// adc's carry conversion then converts to its addend. Add-only: a subtract's borrow reads
    /// the pre-subtract register and keeps the <c>&lt;</c> relation.</summary>
    private static bool IsZeroCarryRelation(ValueNode relation, RegisterNode low,
            ValueNode blo, bool add) =>
        add && IsConstant(blo, 1)
            && Op(relation, BinaryOperation.EQUAL) is { } equal
            && (equal.Left.Equals(low) && IsZero(equal.Right, low.DataType)
                || equal.Right.Equals(low) && IsZero(equal.Left, low.DataType));

    private static bool IsConstant(ValueNode node, ulong value) =>
        ConstantEvaluation.Resolved(node)?.Value == value;

    /// <summary>Whether <paramref name="lessThan"/> is the carry relation of the form: for add
    /// <c>low &lt; blo</c>, for subtract <c>(low + blo) &lt; blo</c>, both reading the low register
    /// the first instruction just wrote and the same <paramref name="blo"/> operand.</summary>
    private static bool RelationReads(BinaryOperationNode lessThan, RegisterNode low,
        ValueNode blo, bool add) {
        if (add) {
            return lessThan.Left.Equals(low) && lessThan.Right.Equals(blo);
        }
        return lessThan.Right.Equals(blo)
            && lessThan.Left is TypeConversionNode { Value: { } restored }
            && Op(restored, BinaryOperation.PLUS) is { } plus
            && plus.Left.Equals(low) && plus.Right.Equals(blo);
    }

    /// <summary>The operand of <paramref name="node"/> when it is <c>0 - operand</c> on the
    /// operand's type, else null.</summary>
    private static ValueNode? ZeroMinusOperand(ValueNode node) {
        if (Op(node, BinaryOperation.MINUS) is { } minus && IsZero(minus.Left, node.DataType)) {
            return minus.Right;
        }
        return null;
    }

    /// <summary>Whether <paramref name="node"/> is <c>operand != 0</c>, the carry out of a negate.</summary>
    private static bool IsCarryOfResult(ValueNode node, RegisterNode result) =>
        Op(node, BinaryOperation.NOT_EQUAL) is { } relation
        && relation.Left.Equals(result)
        && IsZero(relation.Right, result.DataType);

    /// <summary>Whether <paramref name="node"/> is a conversion to <paramref name="type"/> of the
    /// carry flag <paramref name="flagMask"/>.</summary>
    private static bool IsCarryConversion(ValueNode node, ushort flagMask, DataType type) =>
        node is TypeConversionNode conversion && conversion.DataType.Equals(type)
            && conversion.Value is CpuFlagNode { FlagMask: ushort flag } && flag == flagMask;

    private static bool IsZero(ValueNode node, DataType type) =>
        node is ConstantNode constant && constant.DataType.Equals(type) && constant.Value == 0;

    private static bool IsShiftCount(ValueNode node, ulong count) =>
        node is ConstantNode constant && constant.DataType.Equals(DataType.INT32)
            && constant.Value == count;

    private static (ValueNode Left, ValueNode Right)? Assign(IVisitableAstNode node) {
        if (node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assign) {
            return (assign.Left, assign.Right);
        }
        return null;
    }

    private static BinaryOperationNode? Op(ValueNode node, BinaryOperation operation) {
        if (node is BinaryOperationNode { BinaryOperation: BinaryOperation op } binary && op == operation) {
            return binary;
        }
        return null;
    }

    private static void Rewrite(IrMethod method, IrBlock block, Match match) {
        DataType half = match.Half;
        DataType wide = WideType(half);
        int width = Width(half);
        int last = match.Indices[^1];
        string name = $"{PairName(match.High, match.Low)}_{Suffix(method, match.FirstOrigin)}";
        VariableDeclarationNode declaration = new(wide, name,
            (ValueNode)IrConstantFolder.FoldConstantTerms(IrConstantFolder.Fold(
                Combined(match.Form, half, match.ALo, match.AHi,
                    match.BLo ?? match.ALo, match.BHi ?? match.AHi),
                IrConstantFolder.NoVariableConstants)));
        VariableReferenceNode pair = declaration.Reference;
        List<IrStatement> statements = block.Statements;
        statements[last] = IrStatement.Create(declaration, match.LastOrigin);
        // The first instruction's half write may be absent from the pattern (its shifted value was
        // forwarded into its readers and the register write removed). Emitting it anyway would
        // rebind those readers' register references from the pre-shift version to the new pair's
        // half; skip it like the dead write it replaces.
        bool writeLow = !(match.OmitFirstHalfWrite
            && match.Form is DoublePrecisionForm.ShiftLeft);
        bool writeHigh = !(match.OmitFirstHalfWrite
            && match.Form is DoublePrecisionForm.ShiftRight
                or DoublePrecisionForm.SignedShiftRight);
        int position = last + 1;
        if (writeLow && writeHigh && ParentOf(match.Low, match.High) is { } parent) {
            // The two half writes L = (byte)v; H = (byte)(v >> 8) of one register are X = v: the
            // temporary reads no register, so merging never reads a byte the first write changed.
            statements.Insert(position, IrStatement.Create(
                new BinaryOperationNode(wide, parent, BinaryOperation.ASSIGN, pair),
                match.LastOrigin));
        } else {
            // Fresh write targets: the matched register node can be an operand instance inside
            // the combined value, and a shared node must never double as a write target.
            if (writeLow) {
                statements.Insert(position++, IrStatement.Create(
                    new BinaryOperationNode(half,
                        new RegisterNode(half, match.Low.RegisterIndex), BinaryOperation.ASSIGN,
                        new TypeConversionNode(half, pair)), match.LastOrigin));
            }
            if (writeHigh) {
                statements.Insert(position, IrStatement.Create(
                    new BinaryOperationNode(half,
                        new RegisterNode(half, match.High.RegisterIndex), BinaryOperation.ASSIGN,
                        new TypeConversionNode(half, new BinaryOperationNode(wide, pair,
                            BinaryOperation.RIGHT_SHIFT,
                            new ConstantNode(DataType.INT32, (ulong)width)))), match.LastOrigin));
            }
        }
        for (int i = match.Indices.Count - 2; i >= 0; i--) {
            statements.RemoveAt(match.Indices[i]);
        }
    }

    /// <summary>The pair temporary's name: the high register's name lowercased followed by the low
    /// one's first capital (<c>dxAx</c>, <c>ahAl</c>, <c>edxEax</c>).</summary>
    private static string PairName(RegisterNode high, RegisterNode low) {
        string highName = Registers.ToStringRegister(high.DataType.BitWidth, high.RegisterIndex)
            .ToLowerInvariant();
        string lowName = Registers.ToStringRegister(low.DataType.BitWidth, low.RegisterIndex);
        return highName + char.ToUpperInvariant(lowName[0]) + lowName[1..].ToLowerInvariant();
    }

    private static string Suffix(IrMethod method, ICfgNode origin) {
        if (method.LocalSuffixes.TryGetValue(origin, out string? suffix)) {
            return suffix;
        }
        return origin.Address.Offset.ToString("X4");
    }

    // ------------------------------------------------------------------
    // Logical pair operations (non-adjacent halves)
    // ------------------------------------------------------------------

    /// <summary>The word register pairing with <paramref name="low"/> in the conventional
    /// accumulator pairs: DX is the high half of AX's pair, CX of BX's. Other registers are
    /// not combined: two same-op writes on arbitrary registers may be unrelated computations.</summary>
    private static RegisterNode? PairedHalf(RegisterNode low) => low.RegisterIndex switch {
        0 => new RegisterNode(DataType.UINT16, 2),
        3 => new RegisterNode(DataType.UINT16, 1),
        _ => null
    };

    private static ValueNode UnwrapConversion(ValueNode node) =>
        node is TypeConversionNode { Value: { } inner }
            && inner.DataType.BitWidth == node.DataType.BitWidth ? inner : node;

    /// <summary><c>low = aLo op bLo</c> followed, possibly across unrelated pure statements, by
    /// <c>high = aHi op bHi</c> for AND/OR/XOR, or both halves NOT. The halves are the
    /// conventional DX:AX or CX:BX destination pair; every value the first right side reads must
    /// still resolve to the same version at the second statement, the low write being the only
    /// accounted change. The search stops at any statement that faults, interrupts, touches a
    /// token, reads the new low value or writes a moved source or a destination half.</summary>
    private static Match? TryLogical(IrBlock block, List<Run> runs, int r, IrSsa ssa) {
        Run first = FirstBodyRun(runs[r], block);
        if (first.BodyIndices.Count != 1) {
            return null;
        }
        List<IrStatement> statements = block.Statements;
        int lowIndex = first.BodyIndices[0];
        if (Assign(statements[lowIndex].Node) is not (RegisterNode low, { } lowRight)
            || low is SegmentRegisterNode || low.DataType != DataType.UINT16
            || PairedHalf(low) is not { } high
            || !SafeStatement(statements[lowIndex], low)) {
            return null;
        }
        ValueNode right = UnwrapConversion(lowRight);
        DoublePrecisionForm form;
        ValueNode alo;
        ValueNode? blo;
        if (right is BinaryOperationNode { BinaryOperation: BinaryOperation.BITWISE_AND
                or BinaryOperation.BITWISE_OR or BinaryOperation.BITWISE_XOR,
                DataType: { } opType } binary && opType == DataType.UINT16) {
            form = binary.BinaryOperation switch {
                BinaryOperation.BITWISE_AND => DoublePrecisionForm.And,
                BinaryOperation.BITWISE_OR => DoublePrecisionForm.Or,
                _ => DoublePrecisionForm.Xor
            };
            alo = binary.Left;
            blo = binary.Right;
        } else if (right is UnaryOperationNode { UnaryOperation: UnaryOperation.BITWISE_NOT,
                DataType: { } notType } unary && notType == DataType.UINT16) {
            form = DoublePrecisionForm.Not;
            alo = unary.Value;
            blo = null;
        } else {
            return null;
        }
        if (blo is not null && !IrVerifier.IsPureArm(blo)) {
            return null;
        }
        CpuValues blockedWrites = IrEffectsVisitor.Of(right, false).Reads
            | IrValueSets.Of(low) | IrValueSets.Of(high);
        HashSet<int> accounted = [.. ssa.Definitions(statements[lowIndex])
            .Select(definition => definition.Version)];
        for (int highIndex = lowIndex + 1; highIndex < statements.Count; highIndex++) {
            IrStatement between = statements[highIndex];
            if (IrInstructionIdioms.IsMarker(between)) {
                continue;
            }
            if (Assign(between.Node) is (RegisterNode target, { } highRight)
                && target.Equals(high)) {
                if (!SafeStatement(between, target)
                    || LogicalHalves(UnwrapConversion(highRight), form) is not { } halves
                    || !DoesNotRead(highRight, low)
                    || (halves.BHi is not null && !IrVerifier.IsPureArm(halves.BHi))
                    || !SameSourceVersions(block, lowIndex, highIndex, right, accounted, ssa)
                    || !SameSourceVersions(block, lowIndex, highIndex, highRight, accounted, ssa)) {
                    return null;
                }
                return new Match(form, DataType.UINT16, low, high, alo, halves.AHi,
                    blo, halves.BHi, [lowIndex, highIndex], first.Origin,
                    between.Origin, 1, false);
            }
            if (!IrInstructionIdioms.Joinable(between)
                || (between.Effects.Reads & IrValueSets.Of(low)) != CpuValues.None
                || (between.Effects.MayWrite & blockedWrites) != CpuValues.None) {
                return null;
            }
        }
        return null;
    }

    /// <summary>Whether a consumed statement's effects keep it inside a combined value: no fault,
    /// no delivered interrupt, no token touch and no write beyond its matched register (a nested
    /// flag write, port write or second register write would silently move). A memory read is
    /// safe: the combined statement evaluates it once, and the between-statement scan still
    /// refuses every token access.</summary>
    private static bool SafeStatement(IrStatement statement, RegisterNode target) {
        IrEffects effects = statement.Effects;
        return !effects.MayFault && !effects.DeliversInterrupts
            && effects.TokenWrites == IrTokens.None
            && (effects.TokenReads & ~IrTokens.Memory) == IrTokens.None
            && (effects.MayWrite & ~IrValueSets.Of(target)) == CpuValues.None;
    }

    /// <summary>The operand halves of the high assignment's right side for
    /// <paramref name="form"/>, or null.</summary>
    private static (ValueNode AHi, ValueNode? BHi)? LogicalHalves(ValueNode right,
        DoublePrecisionForm form) {
        if (form == DoublePrecisionForm.Not) {
            return right is UnaryOperationNode { UnaryOperation: UnaryOperation.BITWISE_NOT,
                    DataType: { } notType } unary && notType == DataType.UINT16
                ? (unary.Value, null)
                : null;
        }
        BinaryOperation operation = form switch {
            DoublePrecisionForm.And => BinaryOperation.BITWISE_AND,
            DoublePrecisionForm.Or => BinaryOperation.BITWISE_OR,
            _ => BinaryOperation.BITWISE_XOR
        };
        return right is BinaryOperationNode { BinaryOperation: { } op, DataType: { } opType }
                binary && op == operation && opType == DataType.UINT16
            ? (binary.Left, binary.Right)
            : null;
    }

    /// <summary>Every register value <paramref name="firstRight"/> reads must resolve to the
    /// same version before the first and before the second statement, except a value the first
    /// statement itself redefined (the accounted low-half write the rewrite removes).</summary>
    private static bool SameSourceVersions(IrBlock block, int from, int to, ValueNode firstRight,
        HashSet<int> accounted, IrSsa ssa) {
        uint bits = IrSsaValues.ValueBits(IrEffectsVisitor.Of(firstRight, false).Reads);
        for (int v = 0; v < (int)IrSsaValue.Count; v++) {
            if ((bits & (1u << v)) == 0) {
                continue;
            }
            int before = ssa.VersionBefore(block, from, (IrSsaValue)v);
            int at = ssa.VersionBefore(block, to, (IrSsaValue)v);
            if (before != at && !accounted.Contains(at)) {
                return false;
            }
        }
        return true;
    }

    // ------------------------------------------------------------------
    // The canonical 32-bit product sequence
    // ------------------------------------------------------------------

    /// <summary>Rewrites every occurrence of the compiler's full 32-bit product sequence in
    /// <paramref name="method"/>: <c>savedLow = aLow; savedHigh = aHigh; (outLo,outHi) = aLow *
    /// otherLow; savedProduct = outLo; outLo = savedLow; outLo *= otherHigh; outHi += outLo;
    /// outLo = savedHigh; outLo *= otherLow; outHi += outLo; outLo = savedProduct</c> becomes
    /// one wide multiply declaration and the two half writes. Runs before expression propagation
    /// flattens the copies; returns how many sequences were rewritten.</summary>
    public static int ApplyProducts(IrMethod method, IrSsa ssa) {
        HashSet<ICfgNode> pinnedOrigins = IrInstructionIdioms.PinnedOriginsOf(method);
        int rewritten = 0;
        foreach (IrBlock block in method.Blocks) {
            // Match on the original statements and SSA, then rewrite in reverse order: a rewrite
            // shifts the positions a later match was proven against.
            List<ProductMatch> matches = [];
            List<(int From, int To)> claimed = [];
            List<IrStatement> statements = block.Statements;
            for (int i = 0; i < statements.Count; i++) {
                if (statements[i].Node is not MultiplicationNode {
                        Width: BitWidth.WORD_16, Signed: false }) {
                    continue;
                }
                if (ProductWindow(block, i, ssa) is not { } window) {
                    continue;
                }
                if (claimed.Any(existing => window.First <= existing.To
                        && window.Last >= existing.From)
                    || IrInstructionIdioms.CrossesPinnedOrigin(statements, window.First,
                        window.Last, pinnedOrigins)) {
                    continue;
                }
                claimed.Add((window.First, window.Last));
                matches.Add(window);
            }
            for (int m = matches.Count - 1; m >= 0; m--) {
                RewriteProduct(method, block, matches[m]);
                rewritten++;
            }
        }
        return rewritten;
    }

    /// <summary>The bound roles of a product window.</summary>
    private sealed record ProductMatch(RegisterNode OutputLow, RegisterNode OutputHigh,
        RegisterNode ALow, RegisterNode AHigh, RegisterNode OtherLow, RegisterNode OtherHigh,
        int First, int Last, int[] Consumed);

    private static ProductMatch? ProductWindow(IrBlock block, int multIndex, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        MultiplicationNode multiply = (MultiplicationNode)statements[multIndex].Node;
        int s0 = PreviousEffective(statements, PreviousEffective(statements, multIndex));
        int s1 = PreviousEffective(statements, multIndex);
        if (s0 < 0 || s1 < 0
            || CopyOf(statements[s0]) is not (RegisterNode savedLow, RegisterNode aLow)
            || CopyOf(statements[s1]) is not (RegisterNode savedHigh, RegisterNode aHigh)) {
            return null;
        }
        // The copies must save the unsigned multiply's own input registers: a saved value taken
        // after the first copy could otherwise read an already-overwritten register and the
        // replacement would multiply stale halves.
        if (!aLow.Equals(multiply.Low) || !aHigh.Equals(multiply.High)) {
            return null;
        }
        // One multiply operand is the saved low half; the other is the other pair's low word.
        RegisterNode? otherLow;
        if (multiply.Left is RegisterNode left && left.Equals(aLow)) {
            otherLow = multiply.Right as RegisterNode;
        } else if (multiply.Right is RegisterNode right && right.Equals(aLow)) {
            otherLow = multiply.Left as RegisterNode;
        } else {
            return null;
        }
        if (otherLow is null or SegmentRegisterNode
            || otherLow.DataType != DataType.UINT16
            || aLow.DataType != DataType.UINT16 || aHigh.DataType != DataType.UINT16
            || aLow.RegisterIndex == 4 || aHigh.RegisterIndex == 4) {
            return null;
        }
        RegisterNode outputLow = multiply.Low;
        RegisterNode outputHigh = multiply.High;
        if (!LegalHalves(outputLow, outputHigh) || outputLow.DataType != DataType.UINT16) {
            return null;
        }
        // The eight assignments after the multiply, bound in canonical order.
        int[] steps = new int[8];
        int cursor = multIndex;
        for (int step = 0; step < steps.Length; step++) {
            cursor = IrInstructionIdioms.NextEffective(statements, cursor + 1);
            if (cursor < 0) {
                return null;
            }
            steps[step] = cursor;
        }
        RegisterNode? otherHigh = null;
        if (CopyOf(statements[steps[0]]) is not (RegisterNode savedProduct, RegisterNode copied1)
            || !copied1.Equals(outputLow)
            || CopyOf(statements[steps[1]]) is not (RegisterNode out1, RegisterNode from1)
            || !out1.Equals(outputLow) || !from1.Equals(savedLow)
            || CrossProductOf(statements[steps[2]], outputLow) is not
                (RegisterNode other1, ValueNode _)
            || !CopyTo(statements[steps[3]], outputHigh, outputLow)
            || CopyOf(statements[steps[4]]) is not (RegisterNode out2, RegisterNode from2)
            || !out2.Equals(outputLow) || !from2.Equals(savedHigh)
            || CrossProductOf(statements[steps[5]], outputLow) is not
                (RegisterNode other2, ValueNode _)
            || !other2.Equals(otherLow)
            || !CopyTo(statements[steps[6]], outputHigh, outputLow)
            || CopyOf(statements[steps[7]]) is not (RegisterNode out3, RegisterNode from3)
            || !out3.Equals(outputLow) || !from3.Equals(savedProduct)) {
            return null;
        }
        otherHigh = other1;
        // Every captured role must be a distinct register byte set.
        RegisterNode[] roles = [savedLow, savedHigh, savedProduct, outputLow, outputHigh,
            otherLow, otherHigh];
        for (int i = 0; i < roles.Length; i++) {
            if (roles[i].DataType != DataType.UINT16 || roles[i] is SegmentRegisterNode
                || roles[i].RegisterIndex == 4) {
                return null;
            }
            for (int j = i + 1; j < roles.Length; j++) {
                if ((IrValueSets.Of(roles[i]) & IrValueSets.Of(roles[j])) != CpuValues.None) {
                    return null;
                }
            }
        }
        int first = s0;
        int last = steps[^1];
        // The window may only contain the eleven canonical assignments plus statements that
        // write nothing but status flags (the dead flag helpers of the multiplies and adds).
        List<int> consumed = [];
        for (int i = first; i <= last; i++) {
            if (IrInstructionIdioms.IsMarker(statements[i])) {
                continue;
            }
            consumed.Add(i);
            if (i == s0 || i == s1 || i == multIndex || steps.Contains(i)) {
                continue;
            }
            if ((statements[i].Effects.MayWrite & ~CpuValues.StatusFlags) != CpuValues.None) {
                return null;
            }
        }
        // The final half writes keep the result live; every other version the window defines
        // (saved copies, intermediate products, flags) must die inside it.
        DataType wide = WideType(DataType.UINT16);
        ValueNode replacement = Combined(DoublePrecisionForm.Multiply, DataType.UINT16,
            aLow, aHigh, otherLow, otherHigh);
        bool sealedOk = IrInstructionIdioms.SealWindow(block, first, last, [.. consumed],
            new Dictionary<IrStatement, CpuValues> {
                [statements[steps[7]]] = IrValueSets.Of(outputLow),
                [statements[steps[6]]] = IrValueSets.Of(outputHigh)
            }, replacement, ssa);
        return sealedOk
            ? new ProductMatch(outputLow, outputHigh, aLow, aHigh, otherLow, otherHigh,
                first, last, [.. consumed])
            : null;
    }

    /// <summary>The <c>target = source</c> register copy of <paramref name="statement"/>, or
    /// null.</summary>
    private static (RegisterNode Target, RegisterNode Source)? CopyOf(IrStatement statement) =>
        Assign(statement.Node) is (RegisterNode target, { } right)
            && target is not SegmentRegisterNode
            && UnwrapConversion(right) is RegisterNode source
            && source is not SegmentRegisterNode
            ? (target, source)
            : null;

    /// <summary>Whether <paramref name="statement"/> is <c>target = target + addend</c>.</summary>
    private static bool CopyTo(IrStatement statement, RegisterNode target, RegisterNode addend) =>
        Assign(statement.Node) is (RegisterNode left, { } right)
        && left.Equals(target)
        && UnwrapConversion(right) is BinaryOperationNode {
            BinaryOperation: BinaryOperation.PLUS, Left: { } l, Right: { } r }
        && l.Equals(target) && r.Equals(addend);

    /// <summary>The <c>target = target * other</c> cross multiply of <paramref name="statement"/>:
    /// the other operand's register, or null. Only the low half of the product survives, so a
    /// signed or unsigned multiply of the captured operands is equivalent; the operands may
    /// carry any conversion.</summary>
    private static (RegisterNode Other, ValueNode Product)? CrossProductOf(IrStatement statement,
        RegisterNode target) {
        if (Assign(statement.Node) is not (RegisterNode left, { } right)
            || !left.Equals(target)
            || StripConversions(right) is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.MULTIPLY, Left: { } l, Right: { } r,
                DataType: { } productType } product
            || productType.BitWidth < BitWidth.WORD_16) {
            return null;
        }
        RegisterNode? other = null;
        if (UnwrapRegister(l) is { } lReg && lReg.Equals(target) && UnwrapRegister(r) is { } o) {
            other = o;
        } else if (UnwrapRegister(r) is { } rReg && rReg.Equals(target)
            && UnwrapRegister(l) is { } o2) {
            other = o2;
        }
        return other is not null && other.DataType == DataType.UINT16
            && other is not SegmentRegisterNode
            ? (other, product) : null;
    }

    /// <summary>The operand under every conversion layer, or null when one layer narrows below
    /// 16 bits: a cross multiply keeps only the low half of the product, so widening and
    /// sign-changing conversions of word values do not matter, but a byte truncation before the
    /// multiply would change the multiplied operand itself.</summary>
    private static ValueNode? StripConversions(ValueNode node) {
        while (node is TypeConversionNode conversion) {
            if (conversion.DataType.BitWidth < BitWidth.WORD_16
                || conversion.Value.DataType.BitWidth < BitWidth.WORD_16) {
                return null;
            }
            node = conversion.Value;
        }
        return node;
    }

    private static RegisterNode? UnwrapRegister(ValueNode node) =>
        StripConversions(node) is RegisterNode register ? register : null;

    /// <summary>The index before <paramref name="index"/> that is not a marker, or -1.</summary>
    private static int PreviousEffective(IReadOnlyList<IrStatement> statements, int index) {
        for (int i = index - 1; i >= 0; i--) {
            if (!IrInstructionIdioms.IsMarker(statements[i])) {
                return i;
            }
        }
        return -1;
    }

    private static void RewriteProduct(IrMethod method, IrBlock block, ProductMatch window) {
        DataType half = DataType.UINT16;
        DataType wide = WideType(half);
        int width = Width(half);
        int last = window.Last;
        string name = $"value_{Suffix(method, block.Statements[last].Origin)}";
        VariableDeclarationNode declaration = new(wide, name,
            (ValueNode)IrConstantFolder.FoldConstantTerms(IrConstantFolder.Fold(
                Combined(DoublePrecisionForm.Multiply, half, window.ALow, window.AHigh,
                    window.OtherLow, window.OtherHigh),
                IrConstantFolder.NoVariableConstants)));
        VariableReferenceNode pair = declaration.Reference;
        ICfgNode origin = block.Statements[last].Origin;
        List<IrStatement> statements = block.Statements;
        statements[last] = IrStatement.Create(declaration, origin);
        statements.Insert(last + 1, IrStatement.Create(new BinaryOperationNode(half,
            new RegisterNode(half, window.OutputLow.RegisterIndex), BinaryOperation.ASSIGN,
            new TypeConversionNode(half, pair)), origin));
        statements.Insert(last + 2, IrStatement.Create(new BinaryOperationNode(half,
            new RegisterNode(half, window.OutputHigh.RegisterIndex), BinaryOperation.ASSIGN,
            new TypeConversionNode(half,
                new BinaryOperationNode(wide, pair, BinaryOperation.RIGHT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)width)))), origin));
        for (int i = window.Consumed.Length - 2; i >= 0; i--) {
            statements.RemoveAt(window.Consumed[i]);
        }
    }

    // ------------------------------------------------------------------
    // Counted pair-shift loops
    // ------------------------------------------------------------------

    /// <summary>The replacement of one counted loop: its block's new statement list and
    /// terminator. Plans are gathered before any mutation so every proof below still runs on the
    /// original statements and SSA.</summary>
    private sealed record CountedLoopPlan(IrBlock Block, List<IrStatement> Statements,
        IrTerminator Terminator);

    /// <summary>Plans a single-block counted loop whose body is exactly one pair shift by
    /// one and the count decrement, when the sole outside predecessor proves the count enters
    /// non-zero: <c>do { shl low,1; rcl high,1 } while (--count != 0)</c> becomes one saturated
    /// wide shift, the two half writes and <c>count = 0</c>. Any event check, token, fault or
    /// live flag in or after the body keeps the loop.</summary>
    private static List<CountedLoopPlan> CountedPairShiftLoops(IrMethod method, IrSsa ssa) {
        Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors =
            method.PredecessorEdges();
        List<CountedLoopPlan> plans = [];
        foreach (IrBlock block in method.Blocks) {
            if (CountedLoop(method, block, ssa, predecessors) is { } plan) {
                plans.Add(plan);
            }
        }
        return plans;
    }

    /// <summary>The <c>count relation 0</c> test of <paramref name="condition"/>: the tested
    /// word register and whether the relation is non-zero.</summary>
    private static (RegisterNode Count, bool NonZero)? CountTest(ValueNode condition) {
        if (condition is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.EQUAL or BinaryOperation.NOT_EQUAL } compare) {
            return null;
        }
        RegisterNode? register = compare switch {
            { Left: RegisterNode left, Right: ConstantNode { Value: 0 } } => left,
            { Left: ConstantNode { Value: 0 }, Right: RegisterNode right } => right,
            _ => null
        };
        return register is not null && register is not SegmentRegisterNode
            && register.DataType == DataType.UINT16
            ? (register, compare.BinaryOperation == BinaryOperation.NOT_EQUAL)
            : null;
    }

    private static CountedLoopPlan? CountedLoop(IrMethod method, IrBlock block, IrSsa ssa,
            Dictionary<IrBlock, List<(IrBlock Block, IrEdge Edge)>> predecessors) {
        if (block.Terminator is not IrBranch branch) {
            return null;
        }
        IrEdge? self = null;
        IrEdge? exit = null;
        if (ReferenceEquals(branch.WhenTrue.Target, block)) {
            (self, exit) = (branch.WhenTrue, branch.WhenFalse);
        } else if (ReferenceEquals(branch.WhenFalse.Target, block)) {
            (self, exit) = (branch.WhenFalse, branch.WhenTrue);
        }
        if (self is null || exit is null || self.InsideInstruction || exit.InsideInstruction
            || CountTest(branch.Condition) is not { } test) {
            return null;
        }
        (RegisterNode count, bool nonZero) = test;
        // The self edge must be the non-zero arm: the body runs while the count is not zero.
        if (nonZero != ReferenceEquals(self, branch.WhenTrue)) {
            return null;
        }
        List<(IrBlock Block, IrEdge Edge)> outside = [.. predecessors.GetValueOrDefault(block, [])
            .Where(predecessor => !ReferenceEquals(predecessor.Block, block))];
        if (outside.Count != 1 || outside[0].Edge.InsideInstruction
            || method.Entries.Any(entry => ReferenceEquals(entry.Block, block))) {
            return null;
        }
        if (!ProvenNonZeroCount(outside[0].Block, block, count, ssa)) {
            return null;
        }
        // The body is exactly the pair shift and the count decrement.
        List<Run> runs = RunsOf(block);
        if (runs.Count != 3) {
            return null;
        }
        int decrement = -1;
        for (int i = 0; i < runs.Count; i++) {
            if (runs[i].BodyIndices.Count == 1
                && IsDecrement(block.Statements[runs[i].BodyIndices[0]], count)) {
                decrement = i;
                break;
            }
        }
        if (decrement < 0) {
            return null;
        }
        int r = decrement == 0 ? 1 : 0;
        if (r + 1 >= runs.Count || r + 1 == decrement) {
            return null;
        }
        Match? shift = TryShift(block, runs, r, true) ?? TryShift(block, runs, r, false);
        if (shift is null || shift.OmitFirstHalfWrite
            || (IrValueSets.Of(count)
                & (IrValueSets.Of(shift.Low) | IrValueSets.Of(shift.High))) != CpuValues.None) {
            return null;
        }
        // No check, token, fault or interrupt may be left out of the counted rewrite.
        foreach (Run run in runs) {
            foreach (int index in run.BodyIndices) {
                IrStatement statement = block.Statements[index];
                if (!IrInstructionIdioms.Joinable(statement)
                    || statement.Node is CheckExternalEventsNode) {
                    return null;
                }
            }
        }
        // Every version the body defines must be rewritten below or die inside the block:
        // a flag surviving the loop or a third live register refuses. A version the header phi
        // merges is internal too when the phi result itself stays inside.
        CpuValues rewrittenRegisters = IrValueSets.Of(shift.Low) | IrValueSets.Of(shift.High)
            | IrValueSets.Of(count);
        Dictionary<int, CpuValues> removedBytesOf = [];
        foreach (Run run in runs) {
            foreach (int index in run.BodyIndices) {
                IrStatement statement = block.Statements[index];
                foreach ((IrSsaValue _, int version) in ssa.Definitions(statement)) {
                    if ((IrSsaValues.MaskOf(ssa.Info(version).Value) & rewrittenRegisters)
                            == CpuValues.None) {
                        removedBytesOf[version] = statement.Effects.MustWrite;
                    }
                }
            }
        }
        HashSet<int> inside = [.. removedBytesOf.Keys];
        List<int> pending = [.. inside];
        for (int p = 0; p < pending.Count; p++) {
            int version = pending[p];
            foreach (IrSsaUse use in ssa.LiveUses(version)) {
                if (use.Phi is { } phi) {
                    if (inside.Add(phi.Result)) {
                        pending.Add(phi.Result);
                    }
                    continue;
                }
                if (use.ConditionalNewVersion >= 0) {
                    if (use.Statement is not { } overwriter
                        || overwriter.Effects.MayFault
                        || !removedBytesOf.TryGetValue(version, out CpuValues removedBytes)
                        || (removedBytes
                            & IrSsaValues.MaskOf(ssa.Info(version).Value)
                            & ~overwriter.Effects.MustWrite) != CpuValues.None) {
                        return null;
                    }
                    continue;
                }
                if (use.Statement is not { } site || !block.Statements.Contains(site)) {
                    return null;
                }
            }
        }
        DataType half = shift.Half;
        DataType wide = WideType(half);
        int width = Width(half) * 2;
        ValueNode pair = Pair(wide, shift.Low, shift.High, Width(half));
        ValueNode shifted = SaturatedShift(shift.Form, wide, pair, count, width);
        string name = $"value_{Suffix(method, branch.Origin)}";
        VariableDeclarationNode declaration = new(wide, name, shifted);
        List<IrStatement> kept = [.. block.Statements.Where(IrInstructionIdioms.IsMarker)];
        List<IrStatement> body = [
            IrStatement.Create(declaration, branch.Origin),
            IrStatement.Create(new BinaryOperationNode(half,
                new RegisterNode(half, shift.Low.RegisterIndex), BinaryOperation.ASSIGN,
                new TypeConversionNode(half, declaration.Reference)), branch.Origin),
            IrStatement.Create(new BinaryOperationNode(half,
                new RegisterNode(half, shift.High.RegisterIndex), BinaryOperation.ASSIGN,
                new TypeConversionNode(half, new BinaryOperationNode(wide,
                    declaration.Reference, BinaryOperation.RIGHT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)Width(half))))), branch.Origin),
            IrStatement.Create(new BinaryOperationNode(count.DataType, count,
                BinaryOperation.ASSIGN, new ConstantNode(count.DataType, 0)), branch.Origin)
        ];
        kept.AddRange(body);
        return new CountedLoopPlan(block, kept,
            new IrJump(branch.Origin, new IrEdge(exit.Target, false)));
    }

    /// <summary>The wide shift of <paramref name="pair"/> by <paramref name="count"/>, saturated:
    /// at or beyond the pair width a left or unsigned right shift is zero and a signed right
    /// shift is the sign bit replicated.</summary>
    private static ValueNode SaturatedShift(DoublePrecisionForm form, DataType wide,
            ValueNode pair, RegisterNode count, int width) {
        ConstantNode limit = new(DataType.INT32, (ulong)width);
        BinaryOperationNode atLimit = new(DataType.BOOL, count,
            BinaryOperation.GREATER_THAN_OR_EQUAL, limit);
        BinaryOperationNode shift = new(wide, pair,
            form == DoublePrecisionForm.ShiftLeft ? BinaryOperation.LEFT_SHIFT
                : BinaryOperation.RIGHT_SHIFT,
            new TypeConversionNode(DataType.INT32, count));
        if (form == DoublePrecisionForm.SignedShiftRight) {
            DataType signedWide = DataType.SignedFromBitWidth(wide.BitWidth);
            ValueNode signedPair = new TypeConversionNode(signedWide, pair);
            return new TypeConversionNode(wide, new ConditionalValueNode(signedWide, atLimit,
                new BinaryOperationNode(signedWide, signedPair, BinaryOperation.RIGHT_SHIFT,
                    new ConstantNode(DataType.INT32, (ulong)(width - 1))),
                new BinaryOperationNode(signedWide, signedPair, BinaryOperation.RIGHT_SHIFT,
                    new TypeConversionNode(DataType.INT32, count))));
        }
        return new ConditionalValueNode(wide, atLimit, new ConstantNode(wide, 0), shift);
    }

    /// <summary>Whether <paramref name="statement"/> is <c>count = count - 1</c>.</summary>
    private static bool IsDecrement(IrStatement statement, RegisterNode count) =>
        Assign(statement.Node) is (RegisterNode target, { } right)
        && target.Equals(count)
        && UnwrapConversion(right) is BinaryOperationNode {
            BinaryOperation: BinaryOperation.MINUS, Left: { } l,
            Right: ConstantNode { Value: 1 } } && l.Equals(count);

    /// <summary>Whether the only outside predecessor proves the loop's count enters non-zero:
    /// a <c>count != 0</c> guard reaching the header, or a constant definition in 1..FFFF.</summary>
    private static bool ProvenNonZeroCount(IrBlock predecessor, IrBlock header,
            RegisterNode count, IrSsa ssa) {
        if (predecessor.Terminator is IrBranch guard
            && CountTest(guard.Condition) is { } test && test.Count.Equals(count)) {
            if (test.NonZero && ReferenceEquals(guard.WhenTrue.Target, header)
                || !test.NonZero && ReferenceEquals(guard.WhenFalse.Target, header)) {
                return true;
            }
        }
        int version = ssa.VersionBefore(header, 0, (IrSsaValue)count.RegisterIndex);
        if (ssa.Info(version).Kind == IrSsaDefinitionKind.Phi) {
            // The header phi merges the outside value with the loop-back one: the first
            // iteration's count is the operand coming from the single outside predecessor.
            IrSsaPhi? phi = ssa.Phis.FirstOrDefault(candidate => candidate.Result == version);
            version = phi?.Operands
                .Where(operand => ReferenceEquals(operand.Predecessor, predecessor))
                .Select(operand => operand.Version).SingleOrDefault(-1) ?? -1;
        }
        IrStatement? definition = version >= 0 ? ssa.DefiningStatement(version) : null;
        return definition is not null
            && Assign(definition.Node) is (RegisterNode target, { } right)
            && target.Equals(count)
            && ConstantEvaluation.Resolved(right) is { } constant
            && constant.Value is >= 1 and <= 0xFFFF;
    }
}
