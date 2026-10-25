namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;

using Xunit;

public class DeadFlagAssignmentRemovalTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private static readonly RegisterNode Ah = new(DataType.UINT8, 4);
    private static readonly RegisterNode Cx = new(DataType.UINT16, (int)RegisterIndex.CxIndex);
    private static readonly FlagRegisterNode FlagRegister = new(DataType.UINT32);
    private static readonly MoveIpNextNode MoveIp = new(new ConstantNode(DataType.UINT16, 0x0102));

    [Fact]
    public void ClearCarryWithADeadCarryIsRemoved() {
        // Arrange
        BlockNode instruction = new(FlagAssignment(StatusFlags.Carry, False()), MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        StatementsOf(result).Should().Equal(MoveIp);
    }

    [Fact]
    public void ClearCarryWithALiveCarryStays() {
        // Arrange
        BlockNode instruction = new(FlagAssignment(StatusFlags.Carry, False()), MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.Carry);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    [Fact]
    public void ComplementCarryWithADeadCarryIsRemoved() {
        // Arrange
        BinaryOperationNode complement = FlagAssignment(StatusFlags.Carry,
            new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, new CpuFlagNode((ushort)StatusFlags.Carry)));
        BlockNode instruction = new(complement, MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.Zero);

        // Assert
        StatementsOf(result).Should().Equal(MoveIp);
    }

    [Fact]
    public void CarryLineAfterANegationIsRemovedWhenTheCarryIsDead() {
        // Arrange
        BinaryOperationNode negate = Assign(Ax,
            new BinaryOperationNode(DataType.UINT16, new ConstantNode(DataType.UINT16, 0), BinaryOperation.MINUS, Ax));
        BinaryOperationNode carryLine = FlagAssignment(StatusFlags.Carry,
            new BinaryOperationNode(DataType.BOOL, Ax, BinaryOperation.NOT_EQUAL,
                new ConstantNode(DataType.UINT16, 0)));
        BlockNode instruction = new(negate, carryLine, MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.Zero);

        // Assert
        StatementsOf(result).Should().Equal(negate, MoveIp);
    }

    [Fact]
    public void FlagReadLaterInTheInstructionKeepsTheWrite() {
        // Arrange
        BlockNode instruction = new(FlagAssignment(StatusFlags.Carry, True()),
            Assign(Cx, new FlagRegisterNode(DataType.UINT16)), MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    [Fact]
    public void StoreAhIntoFlagsWithNoLiveStatusFlagIsRemoved() {
        // Arrange
        BlockNode instruction = new(StoreAhIntoFlags(), MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        StatementsOf(result).Should().Equal(MoveIp);
    }

    [Fact]
    public void StoreAhIntoFlagsWithALiveOverflowStays() {
        // Arrange
        BlockNode instruction = new(StoreAhIntoFlags(), MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.Overflow);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    [Fact]
    public void FlagAssignmentFromAVariableStays() {
        // Arrange
        BinaryOperationNode fromVariable = FlagAssignment(StatusFlags.Carry,
            new VariableReferenceNode(DataType.BOOL, "finalCarry"));
        BlockNode instruction = new(fromVariable, MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    [Fact]
    public void FlagAssignmentReadingMemoryStays() {
        // Arrange
        BinaryOperationNode memoryWord = new(DataType.BOOL, new CpuFlagNode((ushort)StatusFlags.Carry),
            BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.BOOL,
                new AbsolutePointerNode(DataType.UINT16, new ConstantNode(DataType.UINT32, 0x1234)),
                BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.UINT16, 0)));
        BlockNode instruction = new(memoryWord, MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    [Fact]
    public void FlagRegisterLoadFromTheStackStays() {
        // Arrange
        BinaryOperationNode popf = new(DataType.UINT16, new FlagRegisterNode(DataType.UINT16),
            BinaryOperation.ASSIGN, new MethodCallValueNode(DataType.UINT16, "Stack", "Pop16"));
        BlockNode instruction = new(popf, MoveIp);

        // Act
        IVisitableAstNode result = DeadFlagAssignmentRemoval.Remove(instruction, StatusFlags.None);

        // Assert
        result.Should().BeSameAs(instruction);
    }

    private static BinaryOperationNode StoreAhIntoFlags() =>
        new(DataType.UINT32, FlagRegister, BinaryOperation.ASSIGN,
            new BinaryOperationNode(DataType.UINT32,
                new BinaryOperationNode(DataType.UINT32, FlagRegister, BinaryOperation.BITWISE_AND,
                    new ConstantNode(DataType.UINT32, 0xFFFFFF00)),
                BinaryOperation.BITWISE_OR,
                new TypeConversionNode(DataType.UINT32, Ah)));

    private static ConstantNode False() => new(DataType.BOOL, 0);

    private static ConstantNode True() => new(DataType.BOOL, 1);

    private static BinaryOperationNode FlagAssignment(StatusFlags flag, ValueNode value) =>
        new(DataType.BOOL, new CpuFlagNode((ushort)flag), BinaryOperation.ASSIGN, value);

    private static BinaryOperationNode Assign(ValueNode destination, ValueNode value) =>
        new(DataType.UINT16, destination, BinaryOperation.ASSIGN, value);

    private static IReadOnlyList<IVisitableAstNode> StatementsOf(IVisitableAstNode node) =>
        node is BlockNode block ? block.Statements : [];
}
