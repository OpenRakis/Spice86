namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Xunit;

/// <summary>
/// Tests that MemoryWriteDetectorVisitor correctly detects memory writes
/// in AST trees regardless of nesting structure.
/// </summary>
public class MemoryWriteDetectorVisitorTest {
    private static readonly ConstantNode Address = new(DataType.UINT32, 0x1000);
    private static readonly ConstantNode Value = new(DataType.UINT16, 42);
    private static readonly RegisterNode Register = new(DataType.UINT16, 0);

    [Fact]
    public void AssignToAbsolutePointer_ReturnsTrue() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(assign);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AssignToSegmentedPointer_ReturnsTrue() {
        // Arrange
        SegmentedPointerNode pointer = new(DataType.UINT16, new SegmentRegisterNode(0), null, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(assign);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AssignToRegister_ReturnsFalse() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, Register, BinaryOperation.ASSIGN, Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(assign);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void NonAssignBinaryOp_ReturnsFalse() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode add = new(DataType.UINT16, pointer, BinaryOperation.PLUS, Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(add);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void BlockWithMemoryWrite_ReturnsTrue() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);
        BlockNode block = new(assign);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(block);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void BlockWithoutMemoryWrite_ReturnsFalse() {
        // Arrange
        BinaryOperationNode assign = new(DataType.UINT16, Register, BinaryOperation.ASSIGN, Value);
        BlockNode block = new(assign);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(block);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IfElseWithMemoryWriteInTrueCase_ReturnsTrue() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);
        IfElseNode ifElse = new(new ConstantNode(DataType.BOOL, 1), assign, new BlockNode());

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(ifElse);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IfElseWithMemoryWriteInFalseCase_ReturnsTrue() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);
        IfElseNode ifElse = new(new ConstantNode(DataType.BOOL, 1), new BlockNode(), assign);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(ifElse);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void WhileWithMemoryWriteInBody_ReturnsTrue() {
        // Arrange
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);
        WhileNode whileNode = new(new ConstantNode(DataType.BOOL, 1), new BlockNode(assign));

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(whileNode);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void NestedIfInsideBlock_ReturnsTrue() {
        // Arrange — a memory write nested inside an if, inside a block
        AbsolutePointerNode pointer = new(DataType.UINT16, Address);
        BinaryOperationNode assign = new(DataType.UINT16, pointer, BinaryOperation.ASSIGN, Value);
        IfElseNode ifNode = new(new ConstantNode(DataType.BOOL, 1), assign, new BlockNode());
        BlockNode block = new(ifNode);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(block);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void MethodCallNode_ReturnsFalse() {
        // Arrange — a method call with no memory write
        MethodCallNode methodCall = new("Alu16", "Add", Register, Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(methodCall);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VariableDeclaration_ReturnsFalse() {
        // Arrange
        VariableDeclarationNode varDecl = new(DataType.UINT16, "temp", Value);

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(varDecl);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EmptyBlock_ReturnsFalse() {
        // Arrange
        BlockNode block = new();

        // Act
        bool result = MemoryWriteDetectorVisitor.ContainsMemoryWrite(block);

        // Assert
        result.Should().BeFalse();
    }
}
