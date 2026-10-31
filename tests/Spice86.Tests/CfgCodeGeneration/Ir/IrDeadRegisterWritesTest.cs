namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The IR dead register write removal on real parsed instructions: a plain register write nobody
/// reads disappears, the same write stays when a later statement reads the register, and SP, entry
/// statements, memory reads and writes that still feed a later partial definition through their other
/// bytes are never touched.
/// </summary>
public sealed class IrDeadRegisterWritesTest {
    private static readonly int Bx = (int)RegisterIndex.BxIndex;

    private static IrMethod DeadRegisterWritten(params CfgBlock[] blocks) {
        IrMethod method = Lift(blocks);
        IrPassPipeline.Run(method, IrPass.DeadRegisterWrites, verify: true);
        return method;
    }

    private static List<BinaryOperationNode> AssignmentsTo(IrMethod method, int registerIndex) =>
        AssignmentsMatching(method, registerIndex, _ => true);

    private static List<BinaryOperationNode> AssignmentsTo(IrMethod method, int registerIndex,
        DataType dataType) =>
        AssignmentsMatching(method, registerIndex, register => register.DataType == dataType);

    private static List<BinaryOperationNode> AssignmentsMatching(IrMethod method, int registerIndex,
        Func<RegisterNode, bool> registerMatches) =>
        [.. Nodes(method).OfType<BinaryOperationNode>()
            .Where(node => node.BinaryOperation == BinaryOperation.ASSIGN
                && node.Left is RegisterNode register
                && register.RegisterIndex == registerIndex
                && registerMatches(register))];

    private static bool AssignsConstant(BinaryOperationNode assignment, ulong value) =>
        IrAstChildren.Descendants(assignment.Right)
            .OfType<ConstantNode>().Any(constant => constant.Value == value);

    private static bool IsBxAssignment(IVisitableAstNode node, ulong value) =>
        node is BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN, Left: RegisterNode register }
            assignment && register.RegisterIndex == Bx && AssignsConstant(assignment, value);

    [Fact]
    public void MovWhoseRegisterIsRewrittenBeforeAnyReadIsRemoved() {
        // Arrange: nop / mov bx, 5 / mov bx, 7 / ret - the nop is the entry
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x90]),
            (0x0001, [0xBB, 0x05, 0x00]),
            (0x0004, [0xBB, 0x07, 0x00]),
            (0x0007, [0xC3]));

        // Act
        IrMethod method = DeadRegisterWritten(block);

        // Assert
        AssignmentsTo(method, Bx).Should().ContainSingle()
            .Which.Should().Match<BinaryOperationNode>(assignment => AssignsConstant(assignment, 7));
    }

    [Fact]
    public void MovRewrittenAfterAnEventCheckStays() {
        // Arrange: nop / mov bx, 5 / jnz mov bx, 7 / mov bx, 7 / ret - the second mov is a jump
        // target, so it opens a block; an event check at its head lets an interrupt handler read
        // the first value, so neither write is dead
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x90]),
            (0x0001, [0xBB, 0x05, 0x00]),
            (0x0004, [0x75, 0x00]),
            (0x0006, [0xBB, 0x07, 0x00]),
            (0x0009, [0xC3]));
        IrMethod method = Lift(block);
        IrBlock irBlock = method.Blocks.Single(candidate => candidate.Statements.Any(
            statement => IsBxAssignment(statement.Node, 7)));
        ICfgNode origin = irBlock.Statements.First(statement => IsBxAssignment(statement.Node, 7)).Origin;
        irBlock.Statements.Insert(0, IrStatement.Create(
            new CheckExternalEventsNode(origin.Address), origin));

        // Act
        IrPassPipeline.Run(method, IrPass.DeadRegisterWrites, verify: true);

        // Assert
        AssignmentsTo(method, Bx).Should().HaveCount(2);
        AssignmentsTo(method, Bx).Should().Contain(assignment => AssignsConstant(assignment, 5));
        AssignmentsTo(method, Bx).Should().Contain(assignment => AssignsConstant(assignment, 7));
    }

    [Fact]
    public void MovWhoseRegisterReachesTheReturnStays() {
        // Arrange: mov bx, 7 / ret - the ret reads BX
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0xBB, 0x07, 0x00]),
            (0x0003, [0xC3]));

        // Act
        IrMethod method = DeadRegisterWritten(block);

        // Assert
        AssignmentsTo(method, Bx).Should().ContainSingle(assignment =>
            AssignsConstant(assignment, 7));
    }

    /// <summary>
    /// The entry instruction's write is dead too but entries are never touched, and SP is never dropped.
    /// </summary>
    [Theory]
    [InlineData(false, new byte[] { 0xBB, 0x05, 0x00 }, new byte[] { 0xBB, 0x07, 0x00 }, RegisterIndex.BxIndex)]
    [InlineData(true, new byte[] { 0xBC, 0x05, 0x00 }, new byte[] { 0xBC, 0x07, 0x00 }, RegisterIndex.SpIndex)]
    public void TwoWritesToAProtectedRegisterStay(bool nopEntry, byte[] firstMov, byte[] secondMov,
        RegisterIndex register) {
        // Arrange
        TestInstructionHelper helper = new();
        List<(ushort Offset, byte[] Bytes)> instructions = [];
        ushort first = 0;
        if (nopEntry) {
            instructions.Add((0x0000, [0x90]));
            first = 0x0001;
        }
        instructions.Add((first, firstMov));
        instructions.Add(((ushort)(first + firstMov.Length), secondMov));
        instructions.Add(((ushort)(first + firstMov.Length + secondMov.Length), [0xC3]));
        CfgBlock block = Linear(helper, instructions.ToArray());

        // Act
        IrMethod method = DeadRegisterWritten(block);

        // Assert
        AssignmentsTo(method, (int)register).Should().HaveCount(2);
    }

    [Fact]
    public void DeadMovFromMemoryStays() {
        // Arrange: nop / mov bx, [si] / mov bx, 7 / ret - a memory read is not a plain value
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x90]),
            (0x0001, [0x8B, 0x1C]),
            (0x0003, [0xBB, 0x07, 0x00]),
            (0x0006, [0xC3]));

        // Act
        IrMethod method = DeadRegisterWritten(block);

        // Assert
        AssignmentsTo(method, Bx).Should().Contain(assignment =>
            assignment.Right is SegmentedPointerNode,
            "the memory read is not a plain value and stays");
        AssignmentsTo(method, Bx).Should().Contain(assignment => AssignsConstant(assignment, 7));
    }

    [Fact]
    public void PartialWriteFeedingTheNextWriteThroughItsOtherBytesIsRemoved() {
        // Arrange: nop / mov bl, 5 / mov bx, 7 / ret - the BL write is dead: the BX write
        // overwrites every byte it defined and starts a web of its own
        TestInstructionHelper helper = new();
        CfgBlock block = Linear(helper,
            (0x0000, [0x90]),
            (0x0001, [0xB3, 0x05]),
            (0x0003, [0xBB, 0x07, 0x00]),
            (0x0006, [0xC3]));

        // Act
        IrMethod method = DeadRegisterWritten(block);

        // Assert
        AssignmentsTo(method, Bx, DataType.UINT8).Should().BeEmpty();
        AssignmentsTo(method, Bx).Should().ContainSingle(assignment =>
            AssignsConstant(assignment, 7));
    }
}
