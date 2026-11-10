namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// <see cref="IrWideComparisons"/> packs the two- and three-test decision trees of a 32-bit
/// comparison and inlines the compare setup of an or-test or a range test. The refusals check
/// the graph guards: statements inside a test block, a second predecessor, an
/// inside-instruction edge and a live value all keep the shape untouched.
/// </summary>
public sealed class IrWideComparisonsTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Cx = new(DataType.UINT16, 1);
    private static readonly RegisterNode Dx = new(DataType.UINT16, 2);
    private static readonly RegisterNode Bp = new(DataType.UINT16, 5);
    private static readonly ConstantNode Zero = new(DataType.UINT16, 0);

    private static int _nextOffset;

    [Fact]
    public void SignedThreeWay_BecomesWideLessThan() {
        // Arrange: (short)DX < (short)CX ? T : (DX > CX ? F : (AX <u BP ? T : F))
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(SignedGreater(Dx, Cx), leafF, low));
        IrBlock root = TestBlock(Branch(SignedLess(Dx, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        method.Blocks.Should().NotContain(mid).And.NotContain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        BinaryOperationNode compare = branch.Condition.Should()
            .BeOfType<BinaryOperationNode>().Subject;
        compare.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
        compare.Left.Should().BeOfType<TypeConversionNode>()
            .Which.DataType.Should().Be(DataType.INT32);
        branch.WhenTrue.Target.Should().BeSameAs(leafT);
        branch.WhenFalse.Target.Should().BeSameAs(leafF);
    }

    [Fact]
    public void EqualityChain_BecomesWideNotEqual() {
        // Arrange: DX != CX ? T : (AX != BP ? T : F)
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(Unequal(Ax, Bp), leafT, leafF));
        IrBlock root = TestBlock(Branch(Unequal(Dx, Cx), leafT, low));
        IrMethod method = Method([root, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        method.Blocks.Should().NotContain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.NOT_EQUAL);
        branch.WhenTrue.Target.Should().BeSameAs(leafT);
        branch.WhenFalse.Target.Should().BeSameAs(leafF);
    }

    [Fact]
    public void ThreeWay_StatementInInnerBlock_Stays() {
        // Arrange: a store inside the mid test block is not a removable marker.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(UINT16Greater(Dx, Cx), leafF, low),
            StoreWord(Ax));
        IrBlock root = TestBlock(Branch(UINT16Less(Dx, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: the root cannot absorb mid (the store is not a marker), and mid's own
        // `DX > CX ? F : (AX < BP ? T : F)` tail is a general `&&`, not a relational
        // range - the AST combiner decides that merge, not this pass.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch rootBranch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        rootBranch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
        rootBranch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    [Fact]
    public void ThreeWay_OtherPredecessor_Stays() {
        // Arrange: a second block also jumps to the low test, so it cannot go away.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(UINT16Greater(Dx, Cx), leafF, low));
        IrBlock extra = TestBlock(Branch(Unequal(Ax, Bp), low, leafF));
        IrBlock root = TestBlock(Branch(UINT16Less(Dx, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, extra, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(0);
        method.Blocks.Should().Contain(low);
    }

    [Fact]
    public void ThreeWay_InsideInstructionEdge_Stays() {
        // Arrange: the root-to-mid edge stays inside one instruction's control flow.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(UINT16Greater(Dx, Cx), leafF, low));
        IrBlock root = TestBlock(new IrBranch(NextInstruction(), UINT16Less(Dx, Cx),
            new IrEdge(leafT, false), new IrEdge(mid, true)));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: the inside-instruction edge keeps the root out of mid, and mid's
        // general `&&` tail is not a range either - nothing folds.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch rootBranch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        rootBranch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
        rootBranch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    [Fact]
    public void OrTest_InlinesAndRemoves() {
        // Arrange: AX = AX | DX; branch AX == 0; both arms overwrite AX, so the or
        // version feeds only the branch.
        IrBlock leafT = Leaf(AssignWord(Ax, Zero)), leafF = Leaf(AssignWord(Ax, Zero));
        IrBlock root = TestBlock(Branch(Equal(Ax, Zero), leafT, leafF),
            AssignWord(Ax, new BinaryOperationNode(DataType.UINT16, Ax,
                BinaryOperation.BITWISE_OR, Dx)));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        root.Statements.Should().OnlyContain(s => IsMarker(s));
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        BinaryOperationNode compare = branch.Condition.Should()
            .BeOfType<BinaryOperationNode>().Subject;
        compare.Left.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.BITWISE_OR);
    }

    [Fact]
    public void RangeTest_InlinesAndPrints() {
        // Arrange: AL = (byte)(AL - 'a'); branch AL >u 25; both arms overwrite AL.
        RegisterNode al = new(DataType.UINT8, 0);
        IrBlock leafT = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0))),
            leafF = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    new ConstantNode(DataType.UINT8, 25)), leafT, leafF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS,
                    new ConstantNode(DataType.UINT8, 'a')))));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
    }

    [Fact]
    public void OrTest_LiveValue_Stays() {
        // Arrange: AX is read after the branch, so AX = AX | DX must stay.
        IrBlock leafF = Leaf(AssignWord(Ax, Zero));
        IrBlock leafT = Leaf(AssignWord(Cx, Ax));
        IrBlock root = TestBlock(Branch(Equal(Ax, Zero), leafT, leafF),
            AssignWord(Ax, new BinaryOperationNode(DataType.UINT16, Ax,
                BinaryOperation.BITWISE_OR, Dx)));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(0);
    }

    [Fact]
    public void ThreeWay_MixedHighSignedness_Stays() {
        // Arrange: signed `high <` followed by unsigned `high >` is not one three-way test.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(UINT16Greater(Dx, Cx), leafF, low));
        IrBlock root = TestBlock(Branch(SignedLess(Dx, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: the root stays a separate signed compare; mid's `> ? F : low` tail is a
        // general `&&` of two different expressions, not a range - nothing folds.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.Left.Should().BeOfType<TypeConversionNode>()
            .Which.DataType.Should().Be(DataType.INT16);
        branch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    [Fact]
    public void ThreeWay_MixedOperandSignedness_Stays() {
        // Arrange: signed `(short)DX < CX` against a plain word - each operand keeps its
        // own signedness across the two high tests, but the two sides of one test
        // disagree, so packing both halves under `(int)` would change the result.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(new BinaryOperationNode(DataType.BOOL,
            new TypeConversionNode(DataType.INT16, Dx),
            BinaryOperation.GREATER_THAN, Cx), leafF, low));
        IrBlock root = TestBlock(Branch(new BinaryOperationNode(DataType.BOOL,
            new TypeConversionNode(DataType.INT16, Dx),
            BinaryOperation.LESS_THAN, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: no wide compare; the root keeps its mixed `(short)DX < CX` shape.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.Left.Should().BeOfType<TypeConversionNode>()
            .Which.DataType.Should().Be(DataType.INT16);
        branch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    [Fact]
    public void ThreeWay_SignedLowOperand_Stays() {
        // Arrange: the low test compares (short)AX, not an unsigned word - the packed low
        // half could not reuse it.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(SignedLess(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(SignedGreater(Dx, Cx), leafF, low));
        IrBlock root = TestBlock(Branch(SignedLess(Dx, Cx), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: no wide compare; mid's general `&&` tail is left to the AST combiner.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.Left.Should().BeOfType<TypeConversionNode>()
            .Which.DataType.Should().Be(DataType.INT16);
        branch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    [Fact]
    public void EqualityChain_InnerClaimedFirst_StaysDisjoint() {
        // Arrange: a three-test chain where the inner block is listed before the outer one.
        // The inner match claims its own tail first, so the outer match must be rejected
        // instead of rewriting a block another match already removed.
        RegisterNode si = new(DataType.UINT16, 6), di = new(DataType.UINT16, 7);
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock b2 = TestBlock(Branch(Unequal(si, di), leafT, leafF));
        IrBlock b1 = TestBlock(Branch(Unequal(Ax, Bp), leafT, b2));
        IrBlock root = TestBlock(Branch(Unequal(Dx, Cx), leafT, b1));
        IrMethod method = Method([b1, b2, root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: b1 folded its own chain; the root keeps its plain `!=` and still reaches b1.
        folded.Should().Be(1);
        method.Blocks.Should().NotContain(b2).And.Contain(b1);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.Left.Should().BeOfType<RegisterNode>();
        branch.WhenFalse.Target.Should().BeSameAs(b1);
    }

    [Fact]
    public void RangeTest_MemorySource_Snapshots() {
        // Arrange: AL = (byte)(mem - 'a'); branch AL >u 25. The single memory read moves
        // into a method-scope variable the substituted condition reads twice.
        RegisterNode al = new(DataType.UINT8, 0);
        ValueNode memory = new SegmentedPointerNode(DataType.UINT8,
            new ConstantNode(DataType.UINT16, 0x2000), null,
            new ConstantNode(DataType.UINT16, 0x10));
        IrBlock leafT = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0))),
            leafF = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    new ConstantNode(DataType.UINT8, 25)), leafT, leafF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, memory, BinaryOperation.MINUS,
                    new ConstantNode(DataType.UINT8, 'a')))));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        VariableDeclarationNode variable = method.MethodVariables.Should()
            .ContainSingle().Subject;
        variable.DataType.Should().Be(DataType.UINT8);
        root.Statements.Any(statement =>
            statement.Node is BinaryOperationNode assign
            && assign.BinaryOperation == BinaryOperation.ASSIGN
            && assign.Left is MethodScopedVariableReferenceNode).Should().BeTrue();
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
    }

    [Fact]
    public void RangeTest_MemorySourceFields_Snapshots() {
        // Arrange: like the constant case, but the SUB/CMP immediates are the real
        // parser's instruction fields - frozen, they still snapshot and fold.
        TestInstructionHelper helper = new();
        InstructionFieldNode subtrahend = FieldOf(
            IrTestPrograms.Parse(helper, 0, 0x2C, 0x61));
        InstructionFieldNode bound = FieldOf(
            IrTestPrograms.Parse(helper, 2, 0x3C, 0x19));
        RegisterNode al = new(DataType.UINT8, 0);
        ValueNode memory = new SegmentedPointerNode(DataType.UINT8,
            new ConstantNode(DataType.UINT16, 0x2000), null,
            new ConstantNode(DataType.UINT16, 0x10));
        IrBlock leafT = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0))),
            leafF = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    bound), leafT, leafF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, memory, BinaryOperation.MINUS,
                    subtrahend))));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        method.MethodVariables.Should().ContainSingle();
        root.Statements.Any(statement =>
            statement.Node is BinaryOperationNode assign
            && assign.BinaryOperation == BinaryOperation.ASSIGN
            && assign.Left is MethodScopedVariableReferenceNode).Should().BeTrue();
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
    }

    [Fact]
    public void RangeTest_MemorySourceMutableField_Stays() {
        // Arrange: the same parsed shape, but the SUB immediate is a mutable field -
        // a memory-resident operand is not the constant bound the range proof needs.
        TestInstructionHelper helper = new();
        InstructionFieldNode subtrahend = FieldOf(
            IrTestPrograms.Parse(helper, 0, 0x2C, 0x61));
        InstructionFieldNode bound = FieldOf(
            IrTestPrograms.Parse(helper, 2, 0x3C, 0x19));
        subtrahend.Field.UseValue = false;
        RegisterNode al = new(DataType.UINT8, 0);
        ValueNode memory = new SegmentedPointerNode(DataType.UINT8,
            new ConstantNode(DataType.UINT16, 0x2000), null,
            new ConstantNode(DataType.UINT16, 0x10));
        IrBlock leafT = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0))),
            leafF = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    bound), leafT, leafF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, memory, BinaryOperation.MINUS,
                    subtrahend))));
        IrMethod method = Method([root, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(0);
    }

    [Fact]
    public void RangeTest_PinnedOriginBetweenWriterAndBranch_Stays() {
        // Arrange: a pinned marker (a mid-block entry at the compare) sits between the
        // writer and the branch - entering there would skip the subtraction.
        RegisterNode al = new(DataType.UINT8, 0);
        IrBlock leafT = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0))),
            leafF = Leaf(AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        CfgInstruction cmp = NextInstruction();
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    new ConstantNode(DataType.UINT8, 25)), leafT, leafF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS,
                    new ConstantNode(DataType.UINT8, 'a')))),
            new InstructionStartNode("cmp"));
        root.Statements[^1] = IrStatement.Create(root.Statements[^1].Node, cmp);
        IrMethod method = Method([root, leafT, leafF], root);
        IrBlock anchor = new(_nextOffset);
        anchor.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), cmp));
        anchor.Terminator = new IrExit(NextInstruction(), new FailAsUntestedNode([]));
        method.StructuralSuccessors[anchor] = [];
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(0);
        root.Statements.Any(statement =>
            statement.Node is BinaryOperationNode {
                BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode })
            .Should().BeTrue();
    }

    private static InstructionFieldNode FieldOf(CfgInstruction instruction) =>
        IrAstChildren.Descendants(instruction.ExecutionAst)
            .OfType<InstructionFieldNode>().Single();

    [Fact]
    public void RangeTest_ByteVersionDyingAtJoinOverwrite_Inlines() {
        // Arrange: AL = (byte)(AL - 'a'); branch AL >u 25. The false arm rewrites AL
        // before the join, so the writer's byte version reaches the join only through
        // the EAX phi; the join's own AL overwrite kills the tracked byte while the
        // phi's AH/upper bytes survive into its new version.
        RegisterNode al = new(DataType.UINT8, 0);
        IrBlock join = JumpTarget(AssignWord(al, new ConstantNode(DataType.UINT8, 0x41)));
        IrBlock armT = JumpTo(join), armF = JumpTo(join,
            AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    new ConstantNode(DataType.UINT8, 25)), armT, armF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS,
                    new ConstantNode(DataType.UINT8, 'a')))));
        IrMethod method = Method([root, armT, armF, join], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(1);
        root.Statements.Should().OnlyContain(s => IsMarker(s));
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LOGICAL_OR);
    }

    [Fact]
    public void RangeTest_ByteVersionReadAfterJoin_Stays() {
        // Arrange: like the inline case, but the join reads AL - the byte survives
        // past the phi, so the writer must stay.
        RegisterNode al = new(DataType.UINT8, 0), bl = new(DataType.UINT8, 3);
        IrBlock join = JumpTarget(AssignWord(bl, al));
        IrBlock armT = JumpTo(join), armF = JumpTo(join,
            AssignWord(al, new ConstantNode(DataType.UINT8, 0)));
        IrBlock root = TestBlock(Branch(
                new BinaryOperationNode(DataType.BOOL, al, BinaryOperation.GREATER_THAN,
                    new ConstantNode(DataType.UINT8, 25)), armT, armF),
            AssignWord(al, new TypeConversionNode(DataType.UINT8,
                new BinaryOperationNode(DataType.UINT8, al, BinaryOperation.MINUS,
                    new ConstantNode(DataType.UINT8, 'a')))));
        IrMethod method = Method([root, armT, armF, join], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert
        folded.Should().Be(0);
    }

    [Fact]
    public void ThreeWay_SignedZeroTest_Stays() {
        // Arrange: `(short)DX == 0` is not the unsigned `DX < 1` the algebra folds - the
        // zero normalization must not reinterpret a signed word.
        IrBlock leafT = Leaf(), leafF = Leaf();
        IrBlock low = TestBlock(Branch(UINT16Less(Ax, Bp), leafT, leafF));
        IrBlock mid = TestBlock(Branch(SignedGreater(Dx, Cx), leafF, low));
        IrBlock root = TestBlock(Branch(
            new BinaryOperationNode(DataType.BOOL,
                new TypeConversionNode(DataType.INT16, Dx),
                BinaryOperation.EQUAL, Zero), leafT, mid));
        IrMethod method = Method([root, mid, low, leafT, leafF], root);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int folded = IrWideComparisons.Apply(method, ssa);

        // Assert: no wide compare at the root; mid's general `&&` tail is not a range
        // either - nothing folds.
        folded.Should().Be(0);
        method.Blocks.Should().Contain(mid).And.Contain(low);
        IrBranch branch = root.Terminator.Should().BeOfType<IrBranch>().Subject;
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.EQUAL);
        branch.WhenFalse.Target.Should().BeSameAs(mid);
    }

    private static IrBlock JumpTo(IrBlock target, params IVisitableAstNode[] extra) {
        IrJump jump = new(NextInstruction(), new IrEdge(target, false));
        IrBlock block = new(_nextOffset);
        foreach (IVisitableAstNode node in extra) {
            block.Statements.Add(IrStatement.Create(node, NextInstruction()));
        }
        block.Terminator = jump;
        return block;
    }

    private static IrBlock JumpTarget(params IVisitableAstNode[] extra) {
        IrBlock block = new(_nextOffset);
        foreach (IVisitableAstNode node in extra) {
            block.Statements.Add(IrStatement.Create(node, NextInstruction()));
        }
        block.Terminator = new IrExit(NextInstruction(), new FailAsUntestedNode([]));
        return block;
    }

    private static bool IsMarker(IrStatement statement) =>
        statement.Node is InstructionStartNode or CommentNode;

    private static IrBranch Branch(ValueNode condition, IrBlock whenTrue, IrBlock whenFalse) =>
        new(NextInstruction(), condition, new IrEdge(whenTrue, false),
            new IrEdge(whenFalse, false));

    private static BinaryOperationNode UINT16Less(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.LESS_THAN, right);

    private static BinaryOperationNode UINT16Greater(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.GREATER_THAN, right);

    private static BinaryOperationNode SignedLess(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, new TypeConversionNode(DataType.INT16, left),
            BinaryOperation.LESS_THAN, new TypeConversionNode(DataType.INT16, right));

    private static BinaryOperationNode SignedGreater(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, new TypeConversionNode(DataType.INT16, left),
            BinaryOperation.GREATER_THAN, new TypeConversionNode(DataType.INT16, right));

    private static BinaryOperationNode Equal(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.EQUAL, right);

    private static BinaryOperationNode Unequal(ValueNode left, ValueNode right) =>
        new(DataType.BOOL, left, BinaryOperation.NOT_EQUAL, right);

    private static BinaryOperationNode AssignWord(ValueNode left, ValueNode right) =>
        new(right.DataType, left, BinaryOperation.ASSIGN, right);

    private static BinaryOperationNode StoreWord(ValueNode value) =>
        new(DataType.UINT16,
            new SegmentedPointerNode(DataType.UINT16,
                new ConstantNode(DataType.UINT16, 0x2000), null,
                new ConstantNode(DataType.UINT16, 0x10)),
            BinaryOperation.ASSIGN, value);

    private static IrBlock TestBlock(IrTerminator terminator,
            params IVisitableAstNode[] extra) {
        IrBlock block = new(_nextOffset);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"),
            terminator.Origin));
        foreach (IVisitableAstNode node in extra) {
            block.Statements.Add(IrStatement.Create(node, NextInstruction()));
        }
        block.Terminator = terminator;
        return block;
    }

    private static IrBlock Leaf(params IVisitableAstNode[] extra) {
        IrBlock block = new(_nextOffset);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"),
            NextInstruction()));
        foreach (IVisitableAstNode node in extra) {
            block.Statements.Add(IrStatement.Create(node, NextInstruction()));
        }
        block.Terminator = new IrExit(NextInstruction(),
            new FailAsUntestedNode([]));
        return block;
    }

    private static IrMethod Method(IrBlock[] blocks, IrBlock entry) =>
        new("M", [.. blocks],
            [new IrEntry(NextInstruction(), entry)],
            new HashSet<ICfgNode>(),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static CfgInstruction NextInstruction() =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000,
            (ushort)(_nextOffset++ * 4)));
}
