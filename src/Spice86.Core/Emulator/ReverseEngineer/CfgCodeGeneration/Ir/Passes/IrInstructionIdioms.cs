namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Rewrites the multi-statement shapes three instruction idioms lower to, on the flat statements of
/// an <see cref="IrMethod"/>. Only the statements lifted from one instruction can form a shape, so a
/// candidate run must sit in one block and share the origin instruction whose
/// <see cref="IrMethod.Shapes"/> entry is <see cref="IrInstructionShape.Exchange"/> or
/// <see cref="IrInstructionShape.LoadFarPointer"/>, or be untagged. An <c>xchg</c> of two registers
/// whose bytes do not overlap becomes a single <see cref="TupleSwapNode"/>; a <c>les</c> / <c>lds</c>
/// quadruple, or the triple <see cref="IrPlainTemporaryPropagation"/> leaves of it, becomes the two
/// direct assignments, refused when the segment read's address reads a
/// byte of the value register; a <c>mul</c> / <c>imul</c> result-declaration triple becomes
/// <c>AX = call</c> for 8 bits or a <see cref="MultiplicationNode"/> for 16 and 32 bits; a
/// segment-register <c>pop</c> pair becomes the same
/// <see cref="StackCallValueNode"/> assignment a general register pop uses. Runs after
/// <see cref="IrPlainTemporaryPropagation"/>.
/// </summary>
internal static partial class IrInstructionIdioms {
    /// <summary>Rewrites the recognized idioms inside every block of <paramref name="method"/>.</summary>
    public static void Apply(IrMethod method) {
        foreach (List<IrStatement> statements in method.Blocks.Select(block => block.Statements)) {
            for (int i = 0; i < statements.Count; i++) {
                if (ShapeOf(method, statements, i, 3) == IrInstructionShape.Exchange
                    && TupleSwap(statements, i) is { } swap) {
                    statements[i] = IrStatement.Create(swap, statements[i].Origin);
                    statements.RemoveRange(i + 1, 2);
                } else if (ShapeOf(method, statements, i, 4) == IrInstructionShape.LoadFarPointer
                    && LoadFarPointerLifted(statements, i) is { } lifted) {
                    statements[i] = IrStatement.Create(lifted.Value, statements[i].Origin);
                    statements[i + 1] = IrStatement.Create(lifted.Segment, statements[i + 1].Origin);
                    statements.RemoveRange(i + 2, 2);
                    i++;
                } else if (ShapeOf(method, statements, i, 3) == IrInstructionShape.LoadFarPointer
                    && LoadFarPointerPropagated(statements, i) is { } propagated) {
                    statements[i] = IrStatement.Create(propagated.Value, statements[i].Origin);
                    statements[i + 1] = IrStatement.Create(propagated.Segment, statements[i + 1].Origin);
                    statements.RemoveRange(i + 2, 1);
                    i++;
                } else if (Multiplication(statements, i) is { } multiplication) {
                    statements[i] = IrStatement.Create(multiplication, statements[i].Origin);
                    statements.RemoveRange(i + 1, 2);
                } else if (ShapeOf(method, statements, i, 2) == IrInstructionShape.None
                    && SegmentPop(statements, i) is { } pop) {
                    statements[i] = IrStatement.Create(pop, statements[i].Origin);
                    statements.RemoveRange(i + 1, 1);
                }
            }
        }
    }

    /// <summary>
    /// The instruction shape of a run of <paramref name="count"/> consecutive statements when they all
    /// come from one instruction, or <see cref="IrInstructionShape.None"/>.
    /// </summary>
    private static IrInstructionShape ShapeOf(IrMethod method, IReadOnlyList<IrStatement> statements,
        int index, int count) {
        if (index + count > statements.Count) {
            return IrInstructionShape.None;
        }
        ICfgNode origin = statements[index].Origin;
        for (int i = index + 1; i < index + count; i++) {
            if (!statements[i].Origin.Equals(origin)) {
                return IrInstructionShape.None;
            }
        }
        return method.Shapes.GetValueOrDefault(origin, IrInstructionShape.None);
    }

    /// <summary>The <c>xchg</c> shape <c>temp = r1; r1 = r2; r2 = temp</c> of two general registers of the
    /// same type whose bytes do not overlap.</summary>
    private static TupleSwapNode? TupleSwap(IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: RegisterNode first } declaration
            || first is SegmentRegisterNode
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Right: RegisterNode second } firstAssign
            || second is SegmentRegisterNode
            || !firstAssign.Left.Equals(first)
            || first.DataType != second.DataType
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Right: VariableReferenceNode reference } secondAssign
            || reference.VariableName != declaration.VariableName
            || !secondAssign.Left.Equals(second)
            || (IrValueSets.Of(first) & IrValueSets.Of(second)) != CpuValues.None) {
            return null;
        }
        return new TupleSwapNode(first, second);
    }

    /// <summary>The <c>les</c> / <c>lds</c> shape <c>V = m1; S = m2; R = V; Seg = S</c>: two temporaries each
    /// read once by the copy right after.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointerLifted(
        IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } valueDeclaration
            || statements[index + 1].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } segmentDeclaration
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                    Right: VariableReferenceNode valueReference }
            || valueReference.VariableName != valueDeclaration.VariableName
            || statements[index + 3].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode,
                    Right: VariableReferenceNode segmentReference } copySegment
            || segmentReference.VariableName != segmentDeclaration.VariableName
            || register is SegmentRegisterNode) {
            return null;
        }
        return LoadFarPointer(valueDeclaration, register, segmentDeclaration.Initializer, copySegment);
    }

    /// <summary>The same shape after <see cref="IrPlainTemporaryPropagation"/> inlined the segment
    /// temporary: <c>V = m1; R = V; Seg = m2</c>.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointerPropagated(
        IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not VariableDeclarationNode
                { Initializer: SegmentedPointerNode or AbsolutePointerNode } valueDeclaration
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                    Right: VariableReferenceNode valueReference }
            || valueReference.VariableName != valueDeclaration.VariableName
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode,
                    Right: SegmentedPointerNode or AbsolutePointerNode } copySegment
            || register is SegmentRegisterNode) {
            return null;
        }
        return LoadFarPointer(valueDeclaration, register, copySegment.Right, copySegment);
    }

    /// <summary>The rewrite both <c>les</c> / <c>lds</c> shapes share: it assigns <c>R = m1</c> before
    /// <c>Seg = m2</c>, so it is refused when the address <c>m2</c> reads a byte of <c>R</c>.</summary>
    private static (BinaryOperationNode Value, BinaryOperationNode Segment)? LoadFarPointer(
        VariableDeclarationNode valueDeclaration, RegisterNode register, ValueNode segmentRead,
        BinaryOperationNode copySegment) {
        if ((IrEffectsVisitor.Of(segmentRead, false).Reads
             & IrValueSets.Of(register)) != CpuValues.None) {
            return null;
        }
        return (new BinaryOperationNode(register.DataType, register, BinaryOperation.ASSIGN,
                valueDeclaration.Initializer),
            copySegment with { Right = segmentRead });
    }

    /// <summary>Whether three consecutive statements share one origin instruction whose display
    /// operation is MUL or IMUL.</summary>
    private static bool MulImulOrigin(IReadOnlyList<IrStatement> statements, int index) {
        if (index + 3 > statements.Count
            || statements[index].Origin is not CfgInstruction
                { DisplayAst.Operation: InstructionOperation.MUL or InstructionOperation.IMUL }
                instruction) {
            return false;
        }
        return statements[index + 1].Origin.Equals(instruction)
            && statements[index + 2].Origin.Equals(instruction);
    }

    /// <summary>The <c>mul</c> / <c>imul</c> shape <c>result = AluN.Mul|Imul(a, b);
    /// high = (T)(result &gt;&gt; width); low = (T)result</c>, with the result variable read by those
    /// two statements only. The 8-bit form becomes <c>AX = call</c> (AX is the wide register of the
    /// low target); the 16- and 32-bit forms become a <see cref="MultiplicationNode"/>.</summary>
    private static IVisitableAstNode? Multiplication(IReadOnlyList<IrStatement> statements, int index) {
        if (index + 3 > statements.Count
            || statements[index].Node is not VariableDeclarationNode
                { Initializer: AluOperationNode
                    { Call: { Operation: AluOperation.Mul or AluOperation.Imul } call } operation } declaration
            || statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode high,
                    Right: TypeConversionNode
                        { Value: BinaryOperationNode
                            { BinaryOperation: BinaryOperation.RIGHT_SHIFT,
                                Left: VariableReferenceNode highSource,
                                Right: ConstantNode highShift } } }
            || highSource.VariableName != declaration.VariableName
            || highShift.Value != (ulong)(int)call.Width
            || statements[index + 2].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode low,
                    Right: TypeConversionNode { Value: VariableReferenceNode lowSource } }
            || lowSource.VariableName != declaration.VariableName
            || ResultReadElsewhere(statements, index, declaration.VariableName)
            || !MulImulOrigin(statements, index)) {
            return null;
        }
        if (call.Width == BitWidth.BYTE_8) {
            if (low.DataType != DataType.UINT8 || high.DataType != DataType.UINT8
                || high.RegisterIndex != low.RegisterIndex + 4) {
                return null;
            }
            RegisterNode wide = new(DataType.UINT16, low.RegisterIndex);
            ValueNode value = call.DataType == DataType.UINT16 ? operation
                : new TypeConversionNode(DataType.UINT16, operation);
            return new BinaryOperationNode(DataType.UINT16, wide, BinaryOperation.ASSIGN, value);
        }
        if (call.Width is not (BitWidth.WORD_16 or BitWidth.DWORD_32)
            || low.RegisterIndex != (int)RegisterIndex.AxIndex
            || high.RegisterIndex != (int)RegisterIndex.DxIndex) {
            return null;
        }
        // Fresh write nodes for the tuple halves: the matched targets may share their node
        // instance with a read elsewhere, and a shared node must never print as a local.
        return new MultiplicationNode(call.Width, call.Operation == AluOperation.Imul,
            UnwrapSignedness((ValueNode)call.CallNode.Arguments[0]),
            UnwrapSignedness((ValueNode)call.CallNode.Arguments[1]),
            new RegisterNode(low.DataType, low.RegisterIndex),
            new RegisterNode(high.DataType, high.RegisterIndex));
    }

    /// <summary>Drops a conversion that changes only the signedness of its operand: the printer casts
    /// the argument to the helper's parameter type, so <c>(short)BX</c> and <c>BX</c> hold the same
    /// bits once cast.</summary>
    private static ValueNode UnwrapSignedness(ValueNode node) =>
        node is TypeConversionNode { Value: { } inner }
            && inner.DataType.BitWidth == node.DataType.BitWidth ? inner : node;

    /// <summary>Whether the result variable is read by a statement outside the matched triple; the
    /// declaration lives in one block, so only this statement list is scanned.</summary>
    private static bool ResultReadElsewhere(IReadOnlyList<IrStatement> statements, int index,
        string variableName) {
        for (int i = 0; i < statements.Count; i++) {
            if (i >= index && i <= index + 2) {
                continue;
            }
            if (IrAstChildren.Descendants(statements[i].Node)
                .OfType<VariableReferenceNode>().Any(reference => reference.VariableName == variableName)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>A combined sequence: the replacement for the last statement and the indices of the
    /// consumed earlier statements, whose instruction markers stay.</summary>
    private sealed record SequenceRewrite(int LastIndex, IVisitableAstNode Node, int[] RemovedIndices);

    /// <summary>
    /// Combines the multi-instruction value sequences lowering leaves in the flat statements of
    /// <paramref name="method"/>, running after dead register writes and before expression
    /// propagation: two byte assignments filling the low and high half of one 16-bit general
    /// register become one whole-register assignment at the second write's origin (partial-register
    /// composition), and three <c>a = a ^ b; b = b ^ a; a = a ^ b</c> register assignments become a
    /// <see cref="TupleSwapNode"/>. Returns the number of combined sequences; a nonzero count means
    /// the caller must rebuild the SSA and its webs.
    /// <para>Legality: the combined statements must be consecutive effective statements of one block
    /// - only <see cref="InstructionStartNode"/> and <see cref="CommentNode"/> markers may sit
    /// between them - so an interrupt, a flag write, a store, a port access or any other statement
    /// in between refuses the match. Every consumed statement must not fault
    /// (<see cref="IrEffects.MayFault"/>), not deliver interrupts
    /// (<see cref="IrEffects.DeliversInterrupts"/>), touch no token and write nothing outside the
    /// matched registers. No statement of the sequence after the first may carry a
    /// <em>pinned origin</em>: the entry instruction of a method entry block or the first
    /// instruction of a block the structural successors tie to an incoming edge - a jump landing
    /// there would skip the earlier write.
    /// </para>
    /// </summary>
    public static int ApplyValueSequences(IrMethod method, IrSsa ssa) {
        HashSet<ICfgNode> pinnedOrigins = PinnedOriginsOf(method);
        int combined = 0;
        foreach (IrBlock block in method.Blocks) {
            combined += ApplyBlock(block, ssa, pinnedOrigins);
        }
        return combined;
    }

    /// <summary>The instruction nodes a statement merge must not span: the node of a method entry
    /// and the first statement origin of every block the structural successors tie to an incoming
    /// edge - a jump landing there would skip the earlier write.</summary>
    internal static HashSet<ICfgNode> PinnedOriginsOf(IrMethod method) {
        HashSet<ICfgNode> pinnedOrigins = [.. method.Entries.Select(entry => entry.Node)];
        foreach (IrBlock block in method.StructuralSuccessors.Keys
                .Concat(method.StructuralSuccessors.Values.SelectMany(successors => successors))) {
            if (block.Statements.FirstOrDefault()?.Origin is { } origin) {
                pinnedOrigins.Add(origin);
            }
        }
        return pinnedOrigins;
    }

    /// <summary>Whether a statement in the sequence after the first carries a pinned origin: a
    /// landing site the merge would hide behind the first write.</summary>
    internal static bool CrossesPinnedOrigin(IReadOnlyList<IrStatement> statements, int first,
        int last, HashSet<ICfgNode> pinnedOrigins) {
        for (int i = first + 1; i <= last; i++) {
            if (pinnedOrigins.Contains(statements[i].Origin)) {
                return true;
            }
        }
        return false;
    }

    private static int ApplyBlock(IrBlock block, IrSsa ssa, HashSet<ICfgNode> pinnedOrigins) {
        List<IrStatement> statements = block.Statements;
        List<SequenceRewrite> rewrites = [];
        for (int i = 0; i < statements.Count; i++) {
            SequenceRewrite? rewrite = ArithmeticSequence(block, i, ssa)
                ?? XorSwap(statements, i)
                ?? PartialPair(block, i, ssa);
            if (rewrite is null || CrossesPinnedOrigin(statements, i, rewrite.LastIndex,
                    pinnedOrigins)) {
                continue;
            }
            rewrites.Add(rewrite);
            i = rewrite.LastIndex;
        }
        if (rewrites.Count == 0) {
            return 0;
        }
        foreach (SequenceRewrite rewrite in rewrites) {
            statements[rewrite.LastIndex] =
                IrStatement.Create(rewrite.Node, statements[rewrite.LastIndex].Origin);
        }
        foreach (int index in rewrites.SelectMany(rewrite => rewrite.RemovedIndices)
                .OrderByDescending(index => index)) {
            statements.RemoveAt(index);
        }
        return rewrites.Count;
    }

    /// <summary>Whether <paramref name="statement"/> carries a printed instruction or only a
    /// layout marker a sequence may cross.</summary>
    internal static bool IsMarker(IrStatement statement) =>
        statement.Node is InstructionStartNode or CommentNode;

    /// <summary>The next statement index after <paramref name="from"/> that is not a marker, or
    /// -1.</summary>
    internal static int NextEffective(IReadOnlyList<IrStatement> statements, int from) {
        for (int i = from; i < statements.Count; i++) {
            if (!IsMarker(statements[i])) {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Whether a candidate statement may join a sequence: no fault, no interrupt
    /// delivery and no token touch.</summary>
    internal static bool Joinable(IrStatement statement) =>
        !statement.Effects.MayFault && !statement.Effects.DeliversInterrupts
        && (statement.Effects.TokenReads | statement.Effects.TokenWrites) == IrTokens.None;

    /// <summary>The <c>target = x ^ y</c> assignment of <paramref name="statement"/>: the register
    /// target and the XOR under any same-width conversion, or null.</summary>
    private static BinaryOperationNode? XorOf(IrStatement statement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegisterNode? target) {
        target = null;
        if (statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                Right: { } right } || register is SegmentRegisterNode) {
            return null;
        }
        ValueNode unwrapped = right is TypeConversionNode { Value: { } value } conversion
            && conversion.DataType.BitWidth == value.DataType.BitWidth ? value : right;
        if (unwrapped is not BinaryOperationNode { BinaryOperation: BinaryOperation.BITWISE_XOR,
                DataType: { } xorType } xor
            || xorType != register.DataType
            || xor.Left.DataType != register.DataType
            || xor.Right.DataType != register.DataType) {
            return null;
        }
        target = register;
        return xor;
    }

    /// <summary>The three-XOR register swap <c>a = a ^ b; b = b ^ a; a = a ^ b</c>: every XOR
    /// argument is the exact <see cref="RegisterNode"/> of <c>a</c> or <c>b</c> at the target's
    /// width - nothing else proves <c>b</c> still holds its start value (constant propagation may
    /// have deleted its initialization). Disjoint byte masks keep the registers' parts from
    /// aliasing, so a == b refuses since the sequence zeroes it instead of swapping.</summary>
    private static SequenceRewrite? XorSwap(IReadOnlyList<IrStatement> statements, int index) {
        int second = NextEffective(statements, index + 1);
        int third = second < 0 ? -1 : NextEffective(statements, second + 1);
        if (third < 0
            || XorOf(statements[index], out RegisterNode? a) is not { } firstXor || a is null
            || XorOf(statements[second], out RegisterNode? b) is not { } secondXor || b is null
            || XorOf(statements[third], out RegisterNode? last) is not { } thirdXor
            || last is null) {
            return null;
        }
        CpuValues maskA = IrValueSets.Of(a);
        CpuValues maskB = IrValueSets.Of(b);
        if (a.DataType != b.DataType || (maskA & maskB) != CpuValues.None) {
            return null;
        }
        bool IsPairXor(BinaryOperationNode xor, RegisterNode x, RegisterNode y) =>
            (xor.Left.Equals(x) && xor.Right.Equals(y))
            || (xor.Left.Equals(y) && xor.Right.Equals(x));
        if (!IsPairXor(firstXor, a, b) || !IsPairXor(secondXor, b, a)
            || !last.Equals(a) || !IsPairXor(thirdXor, a, b)) {
            return null;
        }
        CpuValues allowed = maskA | maskB;
        foreach (int i in new[] { index, second, third }) {
            IrStatement statement = statements[i];
            if (!Joinable(statement)
                || (statement.Effects.MayWrite & ~allowed) != CpuValues.None
                || (statement.Effects.Reads & ~allowed) != CpuValues.None) {
                return null;
            }
        }
        // Fresh write nodes, as in Multiplication: matched targets may share an instance with a
        // read, and a shared node must never print as a local.
        TupleSwapNode swap = new(
            new RegisterNode(a.DataType, a.RegisterIndex),
            new RegisterNode(b.DataType, b.RegisterIndex));
        return new SequenceRewrite(third, swap, [index, second]);
    }

    /// <summary>The <c>byteReg = value</c> assignment of <paramref name="statement"/>: a byte write
    /// to one of the eight x86 byte registers with an exact byte right side.</summary>
    private static bool ByteAssign(IrStatement statement,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RegisterNode? target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ValueNode? value) {
        target = null;
        value = null;
        if (statement.Node is not BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register,
                Right: { } right }
            || register is SegmentRegisterNode || register.DataType != DataType.UINT8
            || register.RegisterIndex > 7 || right.DataType != DataType.UINT8) {
            return false;
        }
        target = register;
        value = right;
        return true;
    }

    /// <summary>The byte writes of two consecutive effective statements fill the two halves of one
    /// 16-bit register: the whole-register assignment at the second write's origin.</summary>
    private static SequenceRewrite? PartialPair(IrBlock block, int index, IrSsa ssa) {
        List<IrStatement> statements = block.Statements;
        int second = NextEffective(statements, index + 1);
        if (second < 0
            || !ByteAssign(statements[index], out RegisterNode? first, out ValueNode? firstValue)
            || !ByteAssign(statements[second], out RegisterNode? secondTarget, out ValueNode? secondValue)
            || first.RegisterIndex == secondTarget.RegisterIndex
            || (first.RegisterIndex & 3) != (secondTarget.RegisterIndex & 3)
            || !IrVerifier.IsPureArm(firstValue) || !IrVerifier.IsPureArm(secondValue)) {
            return null;
        }
        CpuValues firstMask = IrValueSets.Of(first);
        CpuValues allowed = firstMask | IrValueSets.Of(secondTarget);
        foreach (int i in new[] { index, second }) {
            IrStatement statement = statements[i];
            if (!Joinable(statement)
                || (statement.Effects.MayWrite & ~allowed) != CpuValues.None) {
                return null;
            }
        }
        // The second right side observes the first byte write: substitute the first value for its
        // register and refuse any other overlapping read (AX, EAX or an alias) rather than infer it.
        ValueNode substitutedSecond = ReplaceReads(secondValue, first, firstValue);
        if ((IrEffectsVisitor.Of(substitutedSecond, false).Reads & firstMask) != CpuValues.None) {
            return null;
        }
        // The first value itself cannot read the byte its write defines: at the second write the
        // read would see the new value.
        if ((IrEffectsVisitor.Of(firstValue, false).Reads & firstMask) != CpuValues.None) {
            return null;
        }
        // The first value moves to the second write: the SSA values of the matched halves are
        // accounted for by the composition (the SSA value covers the whole register), every other
        // value it reads must hold the same version there.
        if (!ssa.SameVersions(IrEffectsVisitor.Of(firstValue, false).Reads & ~allowed,
                IrTokens.None, (block, index), (block, second))) {
            return null;
        }
        ValueNode lowValue = HighByteReads(first.RegisterIndex < 4 ? firstValue : substitutedSecond);
        ValueNode highValue = HighByteReads(first.RegisterIndex < 4 ? substitutedSecond : firstValue);
        RegisterNode parent = new(DataType.UINT16, first.RegisterIndex & 3);
        // Byte-level conversions, not word ones: a (ushort) around a byte arithmetic right side
        // would lose the byte truncation its value semantics require.
        BinaryOperationNode composed = new(DataType.UINT16, parent, BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.UINT16,
                new TypeConversionNode(DataType.UINT8, lowValue), BinaryOperation.BITWISE_OR,
                new BinaryOperationNode(DataType.UINT16,
                    new TypeConversionNode(DataType.UINT8, highValue),
                    BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, 8))));
        IVisitableAstNode folded =
            IrConstantFolder.Fold(composed, IrConstantFolder.NoVariableConstants);
        return new SequenceRewrite(second, folded, [index]);
    }

    /// <summary><paramref name="node"/> with every read of <paramref name="target"/> replaced by
    /// <paramref name="replacement"/>.</summary>
    private static ValueNode ReplaceReads(ValueNode node, RegisterNode target, ValueNode replacement) =>
        node.Equals(target) ? replacement
            : (ValueNode)IrAstChildren.MapChildren(node,
                child => child is ValueNode value ? ReplaceReads(value, target, replacement) : child);

    /// <summary><paramref name="node"/> with every high-byte register read written as a shift of
    /// its 16-bit parent, so a composed word prints <c>AX &gt;&gt; 8</c> instead of AH.</summary>
    private static ValueNode HighByteReads(ValueNode node) =>
        node is RegisterNode { DataType: { } type, RegisterIndex: { } index } register
            && type == DataType.UINT8 && index is >= 4 and <= 7
            ? new TypeConversionNode(DataType.UINT8, new BinaryOperationNode(DataType.UINT16,
                new RegisterNode(DataType.UINT16, index - 4), BinaryOperation.RIGHT_SHIFT,
                new ConstantNode(DataType.INT32, 8)))
            : (ValueNode)IrAstChildren.MapChildren(node,
                child => child is ValueNode value ? HighByteReads(value) : child);

    /// <summary>The segment-register <c>pop</c> shape <c>Seg = UINT16 [SS:SP]; SP = SP + 2</c>: the
    /// general register pop is already one <see cref="StackCallValueNode"/> assignment, so the
    /// segment form takes the same call with the segment as its target. <c>pop ss</c> and the
    /// 32-bit-operand form (<c>SP + 4</c>) are not matched.</summary>
    private static BinaryOperationNode? SegmentPop(IReadOnlyList<IrStatement> statements, int index) {
        if (statements[index].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: SegmentRegisterNode segment,
                    Right: SegmentedPointerNode
                        { Segment: SegmentRegisterNode pointerSegment, DefaultSegment: null,
                            Offset: RegisterNode stackPointer } pointer }
            || segment.RegisterIndex == (int)SegmentRegisterIndex.SsIndex
            || pointer.DataType != DataType.UINT16
            || pointerSegment.RegisterIndex != (int)SegmentRegisterIndex.SsIndex
            || stackPointer.DataType != DataType.UINT16
            || stackPointer.RegisterIndex != (int)RegisterIndex.SpIndex) {
            return null;
        }
        if (statements[index + 1].Node is not BinaryOperationNode
                { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode advanceTarget,
                    Right: BinaryOperationNode
                        { BinaryOperation: BinaryOperation.PLUS, Left: RegisterNode advanceSource,
                            Right: ConstantNode { Value: 2 } } }
            || !advanceTarget.Equals(stackPointer)
            || !advanceSource.Equals(stackPointer)) {
            return null;
        }
        return new BinaryOperationNode(segment.DataType, segment, BinaryOperation.ASSIGN,
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16));
    }
}
