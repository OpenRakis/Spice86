namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;

using Xunit;

/// <summary>
/// Tests that InterruptEnableDetectorVisitor correctly detects writes to the interrupt flag
/// that may enable it (sti, popf, popfd) in AST trees regardless of nesting structure.
/// </summary>
public class InterruptEnableDetectorVisitorTest {
    private static readonly RegisterNode Register = new(DataType.UINT16, 0);
    private static readonly CpuFlagNode InterruptFlag = new(Flags.Interrupt);
    private static readonly CpuFlagNode DirectionFlag = new(Flags.Direction);

    [Fact]
    public void AssignTrueToInterruptFlag_ReturnsTrue() {
        // Arrange - sti: ASSIGN(CpuFlagNode(Flags.Interrupt), ConstantNode(BOOL, 1))
        ConstantNode trueValue = new(DataType.BOOL, 1);
        BinaryOperationNode assign = new(DataType.BOOL, InterruptFlag, BinaryOperation.ASSIGN, trueValue);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AssignFalseToInterruptFlag_ReturnsFalse() {
        // Arrange - cli: ASSIGN(CpuFlagNode(Flags.Interrupt), ConstantNode(BOOL, 0))
        ConstantNode falseValue = new(DataType.BOOL, 0);
        BinaryOperationNode assign = new(DataType.BOOL, InterruptFlag, BinaryOperation.ASSIGN, falseValue);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void AssignToOtherFlag_ReturnsFalse() {
        // Arrange - std: ASSIGN(CpuFlagNode(Flags.Direction), ConstantNode(BOOL, 1))
        ConstantNode trueValue = new(DataType.BOOL, 1);
        BinaryOperationNode assign = new(DataType.BOOL, DirectionFlag, BinaryOperation.ASSIGN, trueValue);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void AssignToFlagRegister_ReturnsTrue() {
        // Arrange - popf/popfd: ASSIGN(FlagRegisterNode(UINT16), ConstantNode(UINT16, 0))
        FlagRegisterNode flagRegister = new(DataType.UINT16);
        ConstantNode value = new(DataType.UINT16, 0);
        BinaryOperationNode assign = new(DataType.UINT16, flagRegister, BinaryOperation.ASSIGN, value);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void AssignToRegister_ReturnsFalse() {
        // Arrange - mov: ASSIGN(RegisterNode(UINT16, 0), ConstantNode(UINT16, 42))
        BinaryOperationNode assign = new(DataType.UINT16, Register, BinaryOperation.ASSIGN, new ConstantNode(DataType.UINT16, 42));

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ReadOfInterruptFlag_ReturnsFalse() {
        // Arrange - just reading the interrupt flag
        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(InterruptFlag);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void InterruptEnableInBlock_ReturnsTrue() {
        // Arrange - BlockNode(mov assign, sti assign)
        BinaryOperationNode movAssign = new(DataType.UINT16, Register, BinaryOperation.ASSIGN, new ConstantNode(DataType.UINT16, 42));
        ConstantNode trueValue = new(DataType.BOOL, 1);
        BinaryOperationNode stiAssign = new(DataType.BOOL, InterruptFlag, BinaryOperation.ASSIGN, trueValue);
        BlockNode block = new(movAssign, stiAssign);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(block);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void InterruptEnableInFalseArm_ReturnsTrue() {
        // Arrange - IfElseNode(ConstantNode(BOOL, 1), new BlockNode(), sti assign)
        ConstantNode condition = new(DataType.BOOL, 1);
        BlockNode trueCase = new();
        ConstantNode trueValue = new(DataType.BOOL, 1);
        BinaryOperationNode stiAssign = new(DataType.BOOL, InterruptFlag, BinaryOperation.ASSIGN, trueValue);
        IfElseNode ifElse = new(condition, trueCase, stiAssign);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ifElse);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParsedSti_ReturnsTrue() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0xFB);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParsedCli_ReturnsFalse() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0xFA);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ParsedStd_ReturnsFalse() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0xFD);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ParsedPopf_ReturnsTrue() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0x9D);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParsedPopfd_ReturnsTrue() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0x66, 0x9D);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParsedSahf_ReturnsFalse() {
        // Arrange
        IVisitableAstNode ast = ParsedExecutionAst.Of(0x9E);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(ast);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SahfFlagMergePreservingInterruptFlag_ReturnsFalse() {
        // Arrange - (flags & 0xFFFFFF00) | (AH << 0): SAHF merging without IF change
        FlagRegisterNode flagRegister = new(DataType.UINT32);
        ConstantNode mask = new(DataType.UINT32, 0xFFFFFF00UL);
        BinaryOperationNode andNode = new(DataType.UINT32, flagRegister, BinaryOperation.BITWISE_AND, mask);
        TypeConversionNode ahRegister = new(DataType.UINT32, new RegisterNode(DataType.UINT8, 4));
        BinaryOperationNode orNode = new(DataType.UINT32, andNode, BinaryOperation.BITWISE_OR, ahRegister);
        BinaryOperationNode assign = new(DataType.UINT32, flagRegister, BinaryOperation.ASSIGN, orNode);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void FlagMergeClearingInterruptFlag_ReturnsTrue() {
        // Arrange - (flags & 0xFFFFFDFF) | (AH << 0): SAHF merging with IF bit cleared (bit 9 = IF)
        FlagRegisterNode flagRegister = new(DataType.UINT32);
        ConstantNode mask = new(DataType.UINT32, 0xFFFFFDFFUL);
        BinaryOperationNode andNode = new(DataType.UINT32, flagRegister, BinaryOperation.BITWISE_AND, mask);
        TypeConversionNode ahRegister = new(DataType.UINT32, new RegisterNode(DataType.UINT8, 4));
        BinaryOperationNode orNode = new(DataType.UINT32, andNode, BinaryOperation.BITWISE_OR, ahRegister);
        BinaryOperationNode assign = new(DataType.UINT32, flagRegister, BinaryOperation.ASSIGN, orNode);

        // Act
        bool result = InterruptEnableDetectorVisitor.EnablesInterrupts(assign);

        // Assert
        result.Should().BeTrue();
    }
}