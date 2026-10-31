namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR ALU lowering on real parsed instructions: an ALU call whose flags are dead is expanded to
/// plain assignments, a live readable flag keeps its flag write without the call, a live unreadable
/// flag or a faulting instruction keeps the call.
/// </summary>
public sealed class IrAluLoweringTest {
    private static IrMethod LoweredAlu(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.AluLowering, verify: true);
        return method;
    }

    private static bool AssignsRegister(IVisitableAstNode node, int registerIndex) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { RegisterIndex: int index }
        } && index == registerIndex;

    private static bool IsAluNode(IVisitableAstNode node) =>
        node is AluStatementNode or AluOperationNode;

    private static bool IsDivision(IVisitableAstNode node) => node is DivisionNode;

    private static bool IsLeftShiftAssign(IVisitableAstNode node) =>
        node is BinaryOperationNode {
            BinaryOperation: BinaryOperation.ASSIGN,
            Left: RegisterNode { RegisterIndex: (int)RegisterIndex.AxIndex },
            Right: BinaryOperationNode { BinaryOperation: BinaryOperation.LEFT_SHIFT }
        };

    [Fact]
    public void AddWithDeadFlagsIsExpandedToPlainCode() {
        // Arrange: add ax,1 / nop
        TestInstructionHelper helper = new();
        CfgInstruction add = Parse(helper, 0x0000, 0x83, 0xC0, 0x01);
        CfgInstruction nop = Parse(helper, 0x0003, 0x90);
        Chain(add, nop);
        CfgBlock block = Block(1, add, nop);

        // Act
        IrMethod method = LoweredAlu(block);

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
        Nodes(method).Should().Contain(node => AssignsRegister(node, (int)RegisterIndex.AxIndex));
    }

    [Fact]
    public void AddThenJzExpandsAndTheBranchReadsTheResult() {
        // Arrange: add ax,1 / jz +2 - the jz reads the result, not a flag
        TestInstructionHelper helper = new();
        CfgInstruction add = Parse(helper, 0x0000, 0x83, 0xC0, 0x01);
        CfgInstruction jz = Parse(helper, 0x0003, 0x74, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(add, jz);
        CfgBlock addBlock = Block(1, add, jz);
        Branch(jz, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(addBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
        IrBranch branch = method.Blocks.Select(block => block.Terminator).OfType<IrBranch>()
            .Single();
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.EQUAL);
    }

    [Fact]
    public void AddThenJcExpandsAndTheBranchReadsTheCarryRelation() {
        // Arrange: add ax,1 / jc +2
        TestInstructionHelper helper = new();
        CfgInstruction add = Parse(helper, 0x0000, 0x83, 0xC0, 0x01);
        CfgInstruction jc = Parse(helper, 0x0003, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0005, 0x90);
        CfgInstruction taken = Parse(helper, 0x0007, 0x90);
        Chain(add, jc);
        CfgBlock addBlock = Block(1, add, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(addBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
        IrBranch branch = method.Blocks.Select(block => block.Terminator).OfType<IrBranch>()
            .Single();
        branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
    }

    [Fact]
    public void UnusedCmpWithDeadFlagsIsRemoved() {
        // Arrange: cmp ax, bx / nop - a cmp writes only flags, and nothing reads them
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        Chain(cmp, nop);
        CfgBlock block = Block(1, cmp, nop);

        // Act
        IrMethod method = LoweredAlu(block);

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
    }

    [Fact]
    public void CmpThenJcDropsTheCallAndTheBranchReadsTheRelation() {
        // Arrange: cmp ax, bx / jc +2
        TestInstructionHelper helper = new();
        CfgInstruction cmp = Parse(helper, 0x0000, 0x39, 0xD8);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(cmp, jc);
        CfgBlock cmpBlock = Block(1, cmp, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(cmpBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
        IrBranch branch = method.Blocks.Select(block => block.Terminator).OfType<IrBranch>()
            .Single();
        BinaryOperationNode condition = branch.Condition.Should().BeOfType<BinaryOperationNode>()
            .Which;
        condition.BinaryOperation.Should().Be(BinaryOperation.LESS_THAN);
        condition.Left.Should().BeOfType<RegisterNode>();
        condition.Right.Should().BeOfType<RegisterNode>();
    }

    [Fact]
    public void MaskedShiftWithDeadFlagsIsExpanded() {
        // Arrange: shl ax, cl / nop
        TestInstructionHelper helper = new();
        CfgInstruction shl = Parse(helper, 0x0000, 0xD3, 0xE0);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        Chain(shl, nop);
        CfgBlock block = Block(1, shl, nop);

        // Act
        IrMethod method = LoweredAlu(block);

        // Assert
        Nodes(method).Should().NotContain(node => IsAluNode(node));
        Nodes(method).Should().Contain(node => IsLeftShiftAssign(node));
    }

    [Fact]
    public void MaskedShiftWithLiveFlagKeepsTheCall() {
        // Arrange: shl ax, cl / jc +2 - a flag the variable-count shift may write is read by the jc
        TestInstructionHelper helper = new();
        CfgInstruction shl = Parse(helper, 0x0000, 0xD3, 0xE0);
        CfgInstruction jc = Parse(helper, 0x0002, 0x72, 0x02);
        CfgInstruction fallthrough = Parse(helper, 0x0004, 0x90);
        CfgInstruction taken = Parse(helper, 0x0006, 0x90);
        Chain(shl, jc);
        CfgBlock shlBlock = Block(1, shl, jc);
        Branch(jc, fallthrough, taken);

        // Act
        IrMethod method = LoweredAlu(shlBlock, Block(2, fallthrough), Block(3, taken));

        // Assert
        Nodes(method).Should().Contain(node => IsAluNode(node),
            "a flag the variable-count shift may write is read by the jc");
    }

    [Fact]
    public void FaultingDivIsNotRewritten() {
        // Arrange: div bl / nop, with a CPU-fault edge to a handler partition
        TestInstructionHelper helper = new();
        CfgInstruction div = Parse(helper, 0x0000, 0xF6, 0xF3);
        CfgInstruction nop = Parse(helper, 0x0002, 0x90);
        CfgInstruction faultHandler = Parse(helper, 0x0100, 0x90);
        Chain(div, nop);
        div.SuccessorsPerType[InstructionSuccessorType.CpuFault] = new HashSet<ICfgNode> { faultHandler };
        CfgBlock block = Block(1, div, nop);
        CfgBlock handlerBlock = Block(2, faultHandler);
        CfgCodePartition main = Partition(1, block);
        CfgCodePartition handlerPartition = Partition(2, handlerBlock);
        CfgCodePartitionTransfer faultTransfer = new() {
            FromPartition = main,
            ToPartition = handlerPartition,
            FromNode = div,
            TargetNode = faultHandler,
            Kind = CfgCodePartitionTransferKind.CpuFault
        };

        // Act
        IReadOnlyList<LiftedMethod> lifted = LiftProgram([main, handlerPartition], [faultTransfer]);
        IrMethod mainMethod = lifted.First().Method;
        IrPassPipeline.Run(mainMethod, IrPass.AluLowering, verify: true);

        // Assert
        Nodes(mainMethod).Should().Contain(node => IsDivision(node),
            "the division runs inside the fault handler and is never rewritten");
    }
}
