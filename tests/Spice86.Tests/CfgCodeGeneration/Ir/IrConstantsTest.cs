namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The register constant propagation on hand-built IR methods: a known constant is substituted in
/// ALU and port arguments, an interrupt check, an opaque may-write or a method entry between the
/// definition and the read blocks it, and a join of equal constants stays known while a join of
/// differing ones does not.
/// </summary>
public sealed class IrConstantsTest {
    [Fact]
    public void AluArgument_ReadsTheKnownConstant() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(AssignAx(instruction, Const(0x1234)));
        block.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(block, 0).Should().BeEquivalentTo(Const(0x1234));
    }

    [Fact]
    public void CheckExternalEvents_BetweenTheWriteAndTheReadBlocksTheSubstitution() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(AssignAx(instruction, Const(0x1234)));
        block.Statements.Add(IrStatement.Create(
            new CheckExternalEventsNode(instruction.Address), instruction));
        block.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(block, 0).Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void OpaqueMayWrite_BetweenTheWriteAndTheReadBlocksTheSubstitution() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(AssignAx(instruction, Const(0x1234)));
        block.Statements.Add(new IrStatement(new CommentNode("opaque"), instruction,
            new IrEffects(IrValues.None, IrValues.None, (IrValues)0xFFFFFFFFUL,
                IrTokens.None, IrTokens.None, false, false)));
        block.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(block, 0).Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void NonPortCall_ArgumentsAreNotSubstituted() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(AssignAx(instruction, Const(0x1234)));
        block.Statements.Add(IrStatement.Create(
            new MethodCallNode("Memory", "set_UInt16", Ax(), Const(0)), instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        ((MethodCallNode)block.Statements[1].Node).Arguments[0].Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void OutPort_OnlyThePortArgumentIsSubstituted() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(AssignDx(instruction, Const(0x3F8)));
        block.Statements.Add(IrStatement.Create(
            new PortWriteCallNode(BitWidth.WORD_16, Dx(), Ax()),
            instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        MethodCallNode call = (PortWriteCallNode)block.Statements[1].Node;
        call.Arguments[0].Should().BeEquivalentTo(Const(0x3F8));
        call.Arguments[1].Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void EqualConstantsAtTheJoin_StayKnown() {
        // Arrange: entry branches on a flag, both arms write the same constant
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        entry.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        trueArm.Statements.Add(AssignAx(instruction, Const(5)));
        falseArm.Statements.Add(AssignAx(instruction, Const(5)));
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        join.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(join, 0).Should().BeEquivalentTo(Const(5));
    }

    [Fact]
    public void DifferingConstantsAtTheJoin_AreNotSubstituted() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        entry.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        trueArm.Statements.Add(AssignAx(instruction, Const(5)));
        falseArm.Statements.Add(AssignAx(instruction, Const(7)));
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        join.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(join, 0).Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void DeadArmConstant_DoesNotFeedTheJoinPhi() {
        // Arrange: the false arm is the only executable predecessor of the join; the true arm's
        // edge is structurally equal but belongs to a different, non-executable predecessor.
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        entry.Terminator = new IrBranch(instruction,
            new BinaryOperationNode(DataType.BOOL, Const(5), BinaryOperation.EQUAL, Const(4)),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        trueArm.Statements.Add(AssignAx(instruction, Const(7)));
        falseArm.Statements.Add(AssignAx(instruction, Const(9)));
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        join.Statements.Add(AssignAx(instruction, AluAdd(Ax(), Const(1))));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        RunConstants(method);

        // Assert
        AluArgument(join, 0).Should().BeEquivalentTo(Const(9));
    }







    [Fact]
    public void MovsbBelowAJoinOfCldAndStdKeepsTheDirectionRead() {
        // Arrange: jz arm2; arm1 cld + jmp join; arm2 std + jmp join; join movsb - the direction at
        // the join varies.
        TestInstructionHelper helper = new();
        CfgInstruction jz = IrTestPrograms.Parse(helper, 0x0000, 0x74, 0x03);
        CfgInstruction cld = IrTestPrograms.Parse(helper, 0x0002, 0xFC);
        CfgInstruction jmpFirst = IrTestPrograms.Parse(helper, 0x0003, 0xEB, 0x03);
        CfgInstruction std = IrTestPrograms.Parse(helper, 0x0005, 0xFD);
        CfgInstruction jmpSecond = IrTestPrograms.Parse(helper, 0x0006, 0xEB, 0x00);
        CfgInstruction movsb = IrTestPrograms.Parse(helper, 0x0008, 0xA4);
        CfgInstruction nop = IrTestPrograms.Parse(helper, 0x0009, 0x90);
        jz.Successors.Add(cld);
        jz.Successors.Add(std);
        jz.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { cld, std };
        CfgBlock head = IrTestPrograms.Block(1, jz);
        CfgBlock clearArm = IrTestPrograms.Block(2, cld, jmpFirst);
        CfgBlock setArm = IrTestPrograms.Block(3, std, jmpSecond);
        cld.Successors.Add(jmpFirst);
        cld.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jmpFirst };
        std.Successors.Add(jmpSecond);
        std.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { jmpSecond };
        CfgBlock join = IrTestPrograms.Block(4, movsb, nop);
        jmpFirst.Successors.Add(movsb);
        jmpFirst.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { movsb };
        jmpSecond.Successors.Add(movsb);
        jmpSecond.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { movsb };
        movsb.Successors.Add(nop);
        movsb.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { nop };

        // Act
        string source = IrTestPrograms.Generate(IrTestPrograms.SingleEntry(head), head, clearArm, setArm, join);

        // Assert
        IrTestPrograms.AssertCompiles(source);
        source.Should().Contain("DirectionFlag ? -1 : 1",
            "the join merges cld and std so the direction varies");
    }

    [Fact]
    public void ConstantConditionSelectsTheArm() {
        // Arrange
        ConditionalValueNode node = new(DataType.INT16, new ConstantNode(DataType.BOOL, 1),
            new ConstantNode(DataType.INT16, 4), new ConstantNode(DataType.INT16, 9));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(node, new Dictionary<string, ConstantNode>());

        // Assert
        folded.Should().BeEquivalentTo(new ConstantNode(DataType.INT16, 4));
    }

    [Fact]
    public void EqualArmsSelectTheArm() {
        // Arrange
        RegisterNode arm = Ax();
        ConditionalValueNode node = new(DataType.UINT16, new CpuFlagNode(Flags.Direction),
            arm, new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(node, new Dictionary<string, ConstantNode>());

        // Assert
        folded.Should().Be(arm);
    }

    [Fact]
    public void NegatedConditionSwapsTheArms() {
        // Arrange
        CpuFlagNode flag = new(Flags.Zero);
        ConditionalValueNode node = new(DataType.INT16,
            new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, flag),
            new ConstantNode(DataType.INT16, 1), new ConstantNode(DataType.INT16, 2));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(node, new Dictionary<string, ConstantNode>());

        // Assert
        folded.Should().BeEquivalentTo(new ConditionalValueNode(DataType.INT16, flag,
            new ConstantNode(DataType.INT16, 2), new ConstantNode(DataType.INT16, 1)));
    }

    [Fact]
    public void OneZeroArmsBecomeTheBoolToNumberConversion() {
        // Arrange
        CpuFlagNode flag = new(Flags.Carry);
        ConditionalValueNode node = new(DataType.UINT16, flag,
            new ConstantNode(DataType.UINT16, 1), new ConstantNode(DataType.UINT16, 0));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(node, new Dictionary<string, ConstantNode>());

        // Assert
        folded.Should().BeEquivalentTo(new TypeConversionNode(DataType.UINT16, flag));
    }

    [Fact]
    public void FalseOrConditionSimplifiesLikeABranchCondition() {
        // Arrange: DirectionFlag ? -1 : 1 with a `false || DirectionFlag` condition.
        ConditionalValueNode node = new(DataType.INT16,
            new BinaryOperationNode(DataType.BOOL, new ConstantNode(DataType.BOOL, 0),
                BinaryOperation.LOGICAL_OR, new CpuFlagNode(Flags.Direction)),
            new ConstantNode(DataType.INT16, unchecked((ushort)-1)), new ConstantNode(DataType.INT16, 1));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(node, new Dictionary<string, ConstantNode>());

        // Assert: the shared boolean identity removes the `false ||` before the select folds.
        folded.Should().BeEquivalentTo(new ConditionalValueNode(DataType.INT16,
            new CpuFlagNode(Flags.Direction),
            new ConstantNode(DataType.INT16, unchecked((ushort)-1)), new ConstantNode(DataType.INT16, 1)));
    }

    [Fact]
    public void DirectionFlagSurvivesAnInterruptCheck() {
        // Arrange: DirectionFlag = true; CheckExternalEvents; AX = (DirectionFlag ? -2 : 2) - the
        // event check delivers an interrupt that pushes FLAGS and restores them on iret, so the
        // direction stays known (unlike the general registers).
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(IrStatement.Create(
            new BinaryOperationNode(DataType.BOOL, new CpuFlagNode(Flags.Direction),
                BinaryOperation.ASSIGN, new ConstantNode(DataType.BOOL, 1)), instruction));
        block.Statements.Add(IrStatement.Create(
            new CheckExternalEventsNode(instruction.Address), instruction));
        block.Statements.Add(AssignAx(instruction, DirectionStep16()));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        ((BinaryOperationNode)block.Statements[^1].Node).Right.Should()
            .BeEquivalentTo(new ConstantNode(DataType.INT16, unchecked((ulong)(ushort)-2)));
    }

    [Fact]
    public void FlagRegisterWriteKeepsTheDirectionBit() {
        // Arrange: FlagRegister16 = 0x0400 (DF set); AX = (DirectionFlag ? -2 : 2).
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(IrStatement.Create(
            new BinaryOperationNode(DataType.UINT16, new FlagRegisterNode(DataType.UINT16),
                BinaryOperation.ASSIGN, Const(Flags.Direction)), instruction));
        block.Statements.Add(AssignAx(instruction, DirectionStep16()));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        ((BinaryOperationNode)block.Statements[^1].Node).Right.Should()
            .BeEquivalentTo(new ConstantNode(DataType.INT16, unchecked((ulong)(ushort)-2)));
    }

    [Fact]
    public void VaryingDirectionWriteKeepsTheSelect() {
        // Arrange: DirectionFlag = AX (unknown); the step stays a select.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(IrStatement.Create(
            new BinaryOperationNode(DataType.BOOL, new CpuFlagNode(Flags.Direction),
                BinaryOperation.ASSIGN, Ax()), instruction));
        block.Statements.Add(AssignAx(instruction, DirectionStep16()));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        ((BinaryOperationNode)block.Statements[^1].Node).Right.Should()
            .BeOfType<ConditionalValueNode>();
    }

    [Fact]
    public void ConditionalWithConstantConditionEvaluatesToTheTakenArm() {
        // Arrange
        ConditionalValueNode node = new(DataType.INT16, new ConstantNode(DataType.BOOL, 1),
            Const(3), Const(5));

        // Act
        bool evaluated = ConstantEvaluation.TryEvaluate(node, _ => null, out ulong value);

        // Assert
        evaluated.Should().BeTrue();
        value.Should().Be(3);
    }

    private static ConditionalValueNode DirectionStep16() =>
        AstBuilder.DirectionStep(BitWidth.WORD_16);

    private static IrConstantFacts RunConstants(IrMethod method) {
        IrSsa ssa = IrSsa.Build(method);
        return IrConstants.Apply(method, ssa, method.ToStructuringGraph());
    }

    private static IVisitableAstNode AluArgument(IrBlock block, int argumentIndex) =>
        ((BinaryOperationNode)block.Statements[^1].Node).Right is AluOperationNode alu
            ? alu.Call.CallNode.Arguments[argumentIndex]
            : throw new InvalidOperationException("the last statement is not an ALU assignment");

    private static IrStatement AssignAx(ICfgNode origin, ValueNode value) =>
        Assign(origin, new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex), value);

    private static IrStatement AssignDx(ICfgNode origin, ValueNode value) =>
        Assign(origin, new RegisterNode(DataType.UINT16, (int)RegisterIndex.DxIndex), value);

    private static IrStatement Assign(ICfgNode origin, RegisterNode target, ValueNode value) =>
        IrStatement.Create(new BinaryOperationNode(target.DataType, target,
            BinaryOperation.ASSIGN, value), origin);

    private static AluOperationNode AluAdd(ValueNode left, ValueNode right) =>
        new(new MethodCallValueNode(DataType.UINT16, "Alu16", "Add", left, right),
            FlagEffect.None, AluOperation.Add, BitWidth.WORD_16);

    private static RegisterNode Ax() => new(DataType.UINT16, (int)RegisterIndex.AxIndex);

    private static RegisterNode Dx() => new(DataType.UINT16, (int)RegisterIndex.DxIndex);

    private static ConstantNode Const(ulong value) => new(DataType.UINT16, value);

    /// <summary>
    /// A declaration no statement reads is removed only when its initializer has no side effect: a
    /// stack pop still moves the stack pointer, so the declaration must stay. No lowered instruction
    /// produces this shape today - the lifter only declares temporaries it then reads - so the
    /// condition is locked on hand-built IR.
    /// </summary>
    [Fact]
    public void UnreadDeclarationWithASideEffectingInitializer_IsKept() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(IrStatement.Create(
            new VariableDeclarationNode(DataType.UINT16, "tmp",
                new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16)),
            instruction));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        block.Statements.Should().ContainSingle()
            .Which.Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void UnreadDeclarationWithAPureInitializer_IsRemoved() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(IrStatement.Create(
            new VariableDeclarationNode(DataType.UINT16, "tmp", Const(7)), instruction));
        block.Statements.Add(AssignAx(instruction, Const(1)));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        RunConstants(method);

        // Assert
        block.Statements.Should().ContainSingle()
            .Which.Node.Should().BeOfType<BinaryOperationNode>();
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    [Fact]
    public void FoldedAluCallKeepsItsTypedSubtype() {
        // Arrange: an argument that folds (1 + 1 -> 2) forces the call to rebuild.
        AluCallValueNode call = new(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16,
            Ax(), new BinaryOperationNode(DataType.UINT16, Const(1), BinaryOperation.PLUS, Const(1)));

        // Act
        IVisitableAstNode folded = IrConstantFolder.Fold(call, new Dictionary<string, ConstantNode>());

        // Assert
        folded.Should().BeOfType<AluCallValueNode>().Which.Should().BeEquivalentTo(
            new AluCallValueNode(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16, Ax(), Const(2)));
    }

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
