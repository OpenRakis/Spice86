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
    public static int Apply(IrMethod method) {
        List<(IrBlock Block, List<Match> Matches)> found = [];
        foreach (IrBlock block in method.Blocks) {
            List<Run> runs = RunsOf(block);
            List<Match> matches = [];
            for (int r = 0; r < runs.Count; r++) {
                if (MatchAt(block, runs, r) is { } match) {
                    matches.Add(match);
                    r += match.Runs - 1;
                }
            }
            if (matches.Count > 0) {
                found.Add((block, matches));
            }
        }
        if (found.Count == 0) {
            return 0;
        }
        int rewritten = 0;
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
            _ => new BinaryOperationNode(wide,
                Pair(wide, alo, ahi, width), BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, 1))
        };
    }

    /// <summary>The <c>(T)lo | (T)hi &lt;&lt; width</c> tree of one pair operand, or the 16-bit
    /// register when the two halves are its byte registers.</summary>
    private static ValueNode Pair(DataType wide, ValueNode lo, ValueNode hi, int width) {
        if (lo is RegisterNode lowRegister && hi is RegisterNode highRegister
            && ParentOf(lowRegister, highRegister) is { } parent) {
            return parent;
        }
        return new BinaryOperationNode(wide, new TypeConversionNode(wide, lo),
            BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(wide, new TypeConversionNode(wide, hi),
                BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, (ulong)width)));
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

    private static Match? MatchAt(IrBlock block, List<Run> runs, int r) {
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
        return TryAddSub(block, runs, r, false);
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
        (Run first, Run second, Run third) = (runs[r], runs[r + 1], runs[r + 2]);
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
        (Run first, Run second) = (runs[r], runs[r + 1]);
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
        return new Match(left ? DoublePrecisionForm.ShiftLeft : DoublePrecisionForm.ShiftRight,
            low.DataType, low, high, low, high, null, null, indices,
            first.Origin, second.Origin, 2, first.BodyIndices.Count == 1);
    }

    /// <summary>Validates the carry-out side of the pair: the flag carry assignment whose masked
    /// relation tests the shifted half's boundary bit, plus its optional own shift statement.
    /// Returns the shifted register (null when the shift write was already dead), the shifted
    /// half register and the carry flag mask.</summary>
    private static (RegisterNode? Shifted, RegisterNode ShiftedHalf, ushort Mask)? ShiftCarrySide(
        Run first, IrBlock block, BinaryOperation shift, bool left) {
        if (Assign(block.Statements[first.BodyIndices[0]].Node) is not
                (CpuFlagNode carryFlag, { } carryRelation)) {
            return null;
        }
        RegisterNode? shifted;
        if (first.BodyIndices.Count == 2) {
            if (Assign(block.Statements[first.BodyIndices[1]].Node) is not
                    (RegisterNode shiftedRegister, { } shiftedValue)
                || Op(shiftedValue, shift) is not { } ownShift
                || !ownShift.Left.Equals(shiftedRegister) || !IsShiftCount(ownShift.Right, 1)) {
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
        return (shifted, shiftedHalf, carryFlag.FlagMask);
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
        (Run first, Run second) = (runs[r], runs[r + 1]);
        if (first.BodyIndices.Count != 1 || second.BodyIndices.Count != 1) {
            return null;
        }
        BinaryOperation combine = add ? BinaryOperation.PLUS : BinaryOperation.MINUS;
        if (Assign(block.Statements[first.BodyIndices[0]].Node) is not
                (RegisterNode low, { } lowSum)
            || Op(lowSum, combine) is not { } lowOperation) {
            return null;
        }
        ValueNode alo = lowOperation.Left;
        ValueNode blo = lowOperation.Right;
        if (Assign(block.Statements[second.BodyIndices[0]].Node) is not
                (RegisterNode high, { } highSum)
            || Op(highSum, combine) is not { } highOperation
            || Op(highOperation.Left, combine) is not { } highHalves
            || highOperation.Right is not TypeConversionNode { } carryConversion
            || !carryConversion.DataType.Equals(low.DataType)
            || Op(carryConversion.Value, BinaryOperation.LESS_THAN) is not { } lessThan
            || !RelationReads(lessThan, low, blo, add)) {
            return null;
        }
        ValueNode ahi = highHalves.Left;
        ValueNode bhi = highHalves.Right;
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
            Combined(match.Form, half, match.ALo, match.AHi,
                match.BLo ?? match.ALo, match.BHi ?? match.AHi));
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
            && match.Form is DoublePrecisionForm.ShiftRight);
        int position = last + 1;
        if (writeLow && writeHigh && ParentOf(match.Low, match.High) is { } parent) {
            // The two half writes L = (byte)v; H = (byte)(v >> 8) of one register are X = v: the
            // temporary reads no register, so merging never reads a byte the first write changed.
            statements.Insert(position, IrStatement.Create(
                new BinaryOperationNode(wide, parent, BinaryOperation.ASSIGN, pair),
                match.LastOrigin));
        } else {
            if (writeLow) {
                statements.Insert(position++, IrStatement.Create(
                    new BinaryOperationNode(half, match.Low, BinaryOperation.ASSIGN,
                        new TypeConversionNode(half, pair)), match.LastOrigin));
            }
            if (writeHigh) {
                statements.Insert(position, IrStatement.Create(
                    new BinaryOperationNode(half, match.High, BinaryOperation.ASSIGN,
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
}
