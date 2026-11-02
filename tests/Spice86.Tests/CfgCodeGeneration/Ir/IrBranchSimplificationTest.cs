namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The branch simplification on hand-built IR methods: boolean identities collapse a condition, a
/// constant condition folds the branch to a jump keeping the taken edge, and the blocks the fold
/// leaves unreachable are removed from the method.
/// </summary>
public sealed class IrBranchSimplificationTest {
    [Fact]
    public void ConstantCondition_FoldsToAJumpAndRemovesTheDeadArm() {
        // Arrange: AX = 5, then if (AX == 5) taken else dead
        CfgInstruction instruction = Instruction(0);
        (IrBlock entry, IrBlock taken, IrBlock dead) = Diamond(instruction,
            AxEquals(5));
        entry.Statements.Add(AssignAx(instruction, Const(5)));
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, taken, dead);

        // Act
        (int branches, int _) = Run(method);

        // Assert
        branches.Should().Be(1);
        IrJump jump = entry.Terminator.Should().BeOfType<IrJump>().Subject;
        jump.Edge.Target.Should().Be(taken);
        method.Blocks.Should().BeEquivalentTo([entry, taken]);
    }

    [Fact]
    public void TakenJump_KeepsTheInsideInstructionFlag() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        (IrBlock entry, IrBlock taken, IrBlock dead) = Diamond(instruction, Const(1));
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, taken, dead);

        // Act
        Run(method);

        // Assert
        entry.Terminator.Should().BeOfType<IrJump>().Which.Edge.InsideInstruction.Should().BeTrue();
    }

    [Fact]
    public void FalseOrCondition_BecomesTheOtherOperand() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        CpuFlagNode flag = new(Flags.Carry);
        (IrBlock entry, _, _) = Diamond(instruction,
            new BinaryOperationNode(DataType.BOOL, BoolConst(0), BinaryOperation.LOGICAL_OR, flag));
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            [.. ReachableFrom(entry)]);

        // Act
        Run(method);

        // Assert
        ((IrBranch)entry.Terminator).Condition.Should().BeSameAs(flag);
    }

    [Fact]
    public void TrueAndCondition_BecomesTheOtherOperand() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        CpuFlagNode flag = new(Flags.Carry);
        (IrBlock entry, _, _) = Diamond(instruction,
            new BinaryOperationNode(DataType.BOOL, BoolConst(1), BinaryOperation.LOGICAL_AND, flag));
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            [.. ReachableFrom(entry)]);

        // Act
        Run(method);

        // Assert
        ((IrBranch)entry.Terminator).Condition.Should().BeSameAs(flag);
    }

    [Fact]
    public void TrueOrCondition_FoldsToTrueAndJumpsToTheTrueArm() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        (IrBlock entry, IrBlock taken, IrBlock dead) = Diamond(instruction,
            new BinaryOperationNode(DataType.BOOL, BoolConst(1), BinaryOperation.LOGICAL_OR,
                new CpuFlagNode(Flags.Carry)));
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, taken, dead);

        // Act
        Run(method);

        // Assert
        entry.Terminator.Should().BeOfType<IrJump>().Which.Edge.Target.Should().Be(taken);
        method.Blocks.Should().BeEquivalentTo([entry, taken]);
    }

    [Fact]
    public void NotTrue_FoldsToFalseAndJumpsToTheFalseArm() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        (IrBlock entry, IrBlock taken, IrBlock dead) = Diamond(instruction,
            new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, BoolConst(1)));
        IrMethod method = Method([Entry(instruction, entry)], [instruction], entry, taken, dead);

        // Act
        Run(method);

        // Assert
        entry.Terminator.Should().BeOfType<IrJump>().Which.Edge.Target.Should().Be(dead);
    }

    [Fact]
    public void BlocksOnlyReachableThroughTheDeadArm_AreRemovedToo() {
        // Arrange: dead arm chains through an inner block
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock taken = Block(1);
        IrBlock deadHead = Block(2);
        IrBlock deadInner = Block(3);
        IrBlock deadTail = Block(4);
        entry.Terminator = new IrBranch(instruction, Const(1),
            new IrEdge(taken, false), new IrEdge(deadHead, false));
        taken.Terminator = new IrExit(instruction, ReturnValue());
        deadHead.Terminator = new IrJump(instruction, new IrEdge(deadInner, false));
        deadInner.Terminator = new IrJump(instruction, new IrEdge(deadTail, false));
        deadTail.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, taken, deadHead, deadInner, deadTail);
        // Act
        (int _, int blocksRemoved) = Run(method);

        // Assert
        method.Blocks.Should().BeEquivalentTo([entry, taken]);
        blocksRemoved.Should().Be(3);
    }

    private static (IrBlock Entry, IrBlock Taken, IrBlock Dead) Diamond(
        CfgInstruction instruction, ValueNode condition) {
        IrBlock entry = Block(0);
        IrBlock taken = Block(1);
        IrBlock dead = Block(2);
        entry.Terminator = new IrBranch(instruction, condition,
            new IrEdge(taken, true), new IrEdge(dead, false));
        taken.Terminator = new IrExit(instruction, ReturnValue());
        dead.Terminator = new IrExit(instruction, ReturnValue());
        return (entry, taken, dead);
    }

    private static IEnumerable<IrBlock> ReachableFrom(IrBlock root) {
        HashSet<IrBlock> seen = [root];
        Queue<IrBlock> worklist = new(seen);
        while (worklist.Count > 0) {
            IrBlock block = worklist.Dequeue();
            foreach (IrEdge edge in block.Terminator.Edges) {
                if (!seen.Add(edge.Target)) {
                    continue;
                }
                worklist.Enqueue(edge.Target);
            }
        }
        return seen;
    }

    private static (int BranchesRemoved, int BlocksRemoved) Run(IrMethod method) {
        IrSsa ssa = IrSsa.Build(method);
        IrConstantFacts facts = IrConstants.Apply(method, ssa, method.ToStructuringGraph());
        return IrBranchSimplification.Apply(method, facts);
    }

    private static BinaryOperationNode AxEquals(ulong value) =>
        new(DataType.BOOL, new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex),
            BinaryOperation.EQUAL, new ConstantNode(DataType.UINT16, value));

    private static IrStatement AssignAx(ICfgNode origin, ValueNode value) =>
        IrStatement.Create(new BinaryOperationNode(DataType.UINT16,
            new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex),
            BinaryOperation.ASSIGN, value), origin);

    private static ConstantNode Const(ulong value) => new(DataType.UINT16, value);

    private static ConstantNode BoolConst(ulong value) => new(DataType.BOOL, value);

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
