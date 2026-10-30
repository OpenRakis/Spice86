namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR flag propagation on real parsed instructions: a conditional jump whose flags come from one
/// ALU setter takes the setter's relation, a carry reader takes the carry relation, an unavailable
/// memory operand goes through a <c>flagOperand_</c> temporary, a setter with remaining flag uses is
/// kept, and a phi of two setters refuses.
/// </summary>
public sealed class IrFlagPropagationTest {
    private static IrMethod Propagated(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.FlagPropagation, verify: true);
        return method;
    }

    private static bool IsRegisterRelation(IVisitableAstNode node) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.LESS_THAN,
            Left: RegisterNode,
            Right: RegisterNode
        };

    private static bool IsCarryRead(IVisitableAstNode node) =>
        node is CpuFlagNode { FlagMask: Flags.Carry };

    private static bool IsAluNode(IVisitableAstNode node) =>
        node is AluStatementNode or AluOperationNode;

    private static bool IsFlagOperandDeclaration(IVisitableAstNode node) =>
        node is VariableDeclarationNode declaration
        && declaration.VariableName.StartsWith("flagOperand_");

    private static IrBranch BranchOf(IrMethod method) =>
        method.Blocks.Select(block => block.Terminator).OfType<IrBranch>().Single();

    private static (CfgBlock head, CfgBlock fallthrough, CfgBlock taken) Jcc(
        TestInstructionHelper helper, CfgInstruction before, CfgInstruction jcc,
        ushort fallthroughOffset, ushort takenOffset) {
        CfgInstruction fallthrough = Parse(helper, fallthroughOffset, 0x90);
        CfgInstruction taken = Parse(helper, takenOffset, 0x90);
        Chain(before, jcc);
        CfgBlock head = Block(1, before, jcc);
        Branch(jcc, fallthrough, taken);
        return (head, Block(2, fallthrough), Block(3, taken));
    }

    [Fact]
    public void CmpThenJcRewritesTheConditionToTheUnsignedRelation() {
        // Arrange: cmp ax, bx; jc
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        (CfgBlock head, CfgBlock fallthrough, CfgBlock taken) = Jcc(helper, cmp, jc, 0x0004, 0x0006);

        // Act
        IrMethod method = Propagated(head, fallthrough, taken);

        // Assert
        BinaryOperationNode condition = BranchOf(method).Condition.Should()
            .BeOfType<BinaryOperationNode>().Which;
        condition.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
        condition.Left.Should().BeOfType<RegisterNode>();
        condition.Right.Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void CmpThenJzRewritesTheConditionToTheEqualityRelation() {
        // Arrange: cmp ax, bx; jz
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction jz = Parse(helper, 0x0002, 0x74, 0x02);
        (CfgBlock head, CfgBlock fallthrough, CfgBlock taken) = Jcc(helper, cmp, jz, 0x0004, 0x0006);

        // Act
        IrMethod method = Propagated(head, fallthrough, taken);

        // Assert
        BranchOf(method).Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.EQUAL);
    }

    [Fact]
    public void SetterInAnotherBlockStillFeedsTheRelation() {
        // Arrange: block1 cmp ax, bx + jump block2; block2 jz + targets
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction jz = Parse(helper, 0x0002, 0x74, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(cmp, jz);
        CfgBlock setterBlock = Block(1, cmp);
        CfgBlock jumpBlock = Block(2, jz);
        Branch(jz, fallthrough, taken);
        CfgBlock fallthroughBlock = Block(3, fallthrough);
        CfgBlock takenBlock = Block(4, taken);

        // Act
        IrMethod method = Propagated(setterBlock, jumpBlock, fallthroughBlock, takenBlock);

        // Assert
        BranchOf(method).Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.EQUAL);
    }

    [Fact]
    public void MemoryOperandBlockedByAStoreGoesThroughATemporary() {
        // Arrange: cmp [si], bx; mov [di], ax; jc - the store makes the memory operand unreadable
        // at the jump, so it is read once at the setter into flagOperand_.
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0x1C);
        CfgInstruction store = Parse(helper, 0x0002, 0x89, 0x05);
        CfgInstruction jc = Parse(helper, 0x0004, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0006, 0x90);
        CfgInstruction taken = Parse(helper, 0x0008, 0x90);
        Chain(cmp, store, jc);
        CfgBlock head = Block(1, cmp, store, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = Propagated(head, Block(2, fallthrough), Block(3, taken));

        // Assert
        method.MethodVariables.Concat(Nodes(method))
            .Should().Contain(node => IsFlagOperandDeclaration(node));
    }

    [Fact]
    public void CmpThenPushfKeepsTheSetterButStillRewritesTheJump() {
        // Arrange: cmp ax, bx; pushf; jc - pushf still reads the flags, so the setter stays.
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction pushf = Parse(helper, 0x0002, 0x9C);
        CfgInstruction jc = Parse(helper, 0x0003, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(cmp, pushf, jc);
        CfgBlock head = Block(1, cmp, pushf, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = Propagated(head, Block(2, fallthrough), Block(3, taken));

        // Assert
        BranchOf(method).Condition.Should().BeOfType<BinaryOperationNode>();
        Nodes(method).Should().Contain(node => IsAluNode(node),
            "pushf still reads the flags the cmp writes");
    }

    [Fact]
    public void CarryReaderTakesTheCarryRelation() {
        // Arrange: cmp ax, bx; sbb ax, bx - the sbb reads the carry the cmp defines.
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction sbb = Parse(helper, 0x0002, 0x19, 0xD8);
        CfgInstruction nop = Parse(helper, 0x0004, 0x90);
        Chain(cmp, sbb, nop);
        CfgBlock block = Block(1, cmp, sbb, nop);

        // Act
        IrMethod method = Propagated(block);

        // Assert
        Nodes(method).Should().Contain(node => IsRegisterRelation(node),
            "the carry read becomes the cmp's relation");
        Nodes(method).Should().NotContain(node => IsCarryRead(node));
    }

    [Fact]
    public void PhiOfTwoSettersKeepsTheFlagTest() {
        // Arrange: jz arm2; arm1 cmp ax, bx; arm2 cmp ax, cx; join jc - the carry at the join is a
        // phi of two different setters, so the jump cannot take either relation.
        TestInstructionHelper helper = new();
        CfgInstruction jz = Parse(helper, 0x0000, 0x74, 0x04);
        CfgInstruction cmpFirst = Parse(helper, 0x0002, 0x39, 0xD8);
        CfgInstruction jmpJoin = Parse(helper, 0x0004, 0xEB, 0x02);
        CfgInstruction cmpSecond = Parse(helper, 0x0006, 0x39, 0xC8);
        CfgInstruction jc = Parse(helper, 0x0008, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x000A, 0x90);
        CfgInstruction taken = Parse(helper, 0x000C, 0x90);
        Branch(jz, cmpFirst, cmpSecond);
        CfgBlock head = Block(1, jz);
        CfgBlock firstArm = Block(2, cmpFirst, jmpJoin);
        CfgBlock secondArm = Block(3, cmpSecond);
        Chain(cmpFirst, jmpJoin);
        Chain(jmpJoin, jc);
        Chain(cmpSecond, jc);
        CfgBlock join = Block(4, jc);
        Branch(jc, fallthrough, taken);
        CfgBlock fallthroughBlock = Block(5, fallthrough);
        CfgBlock takenBlock = Block(6, taken);

        // Act
        IrMethod method = Propagated(head, firstArm, secondArm, join,
            fallthroughBlock, takenBlock);

        // Assert
        IrBranch joinBranch = method.Blocks
            .Select(block => block.Terminator).OfType<IrBranch>()
            .Single(branch => branch.Origin.Equals(jc));
        joinBranch.Condition.Should().BeOfType<CpuFlagNode>()
            .Which.FlagMask.Should().Be(Flags.Carry);
        Nodes(method).Should().Contain(node => IsAluNode(node),
            "a phi of two setters cannot take either relation");
    }

    [Fact]
    public void ClcSetterIsNotAnAluCallAndKeepsTheFlagTest() {
        // Arrange: clc; jc - the carry comes from a plain flag write, not an ALU call.
        TestInstructionHelper helper = new();
        CfgInstruction clc = Parse(helper, 0x0000, 0xF8);
        CfgInstruction jc = Parse(helper, 0x0001, 0x72, 0x02);
        (CfgBlock head, CfgBlock fallthrough, CfgBlock taken) = Jcc(helper, clc, jc, 0x0003, 0x0005);

        // Act
        IrMethod method = Propagated(head, fallthrough, taken);

        // Assert
        BranchOf(method).Condition.Should().BeOfType<CpuFlagNode>()
            .Which.FlagMask.Should().Be(Flags.Carry);
    }
}
