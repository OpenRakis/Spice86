using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

namespace Spice86.Tests.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;

using System.Linq.Expressions;

using Xunit;

public class AstExpressionBuilderTest {
    private readonly Memory _memory = new(new(), new Ram(64), new());
    private readonly State _state = new(CpuModel.INTEL_80286);
    private readonly AstExpressionBuilder _astExpressionBuilder = new();

    [Fact]
    public void TestMemoryAddressAbsolute() {
        // Arrange
        ConstantNode one = new ConstantNode(DataType.UINT32, 1);
        AbsolutePointerNode pointerNode = new AbsolutePointerNode(DataType.UINT8, one);

        // Act
        ExecuteAssignment(pointerNode, 0xF8);

        // Assert
        Assert.Equal(0xF8, _memory[1]);
    }

    [Fact]
    public void TestMemoryAddressSegmented() {
        // Arrange
        ConstantNode one = new ConstantNode(DataType.UINT16, 1);
        ConstantNode two = new ConstantNode(DataType.UINT16, 2);
        SegmentedPointerNode pointerNode = new SegmentedPointerNode(DataType.UINT8, one, null, two);

        // Act
        ExecuteAssignment(pointerNode, 0xF8);

        // Assert
        Assert.Equal(0xF8, _memory.UInt8[1, 2]);
    }

    [Fact]
    public void TestRegister() {
        // Arrange
        RegisterNode register = new RegisterNode(DataType.UINT8, (int)RegisterIndex.AxIndex);

        // Act
        ExecuteAssignment(register, 0xF8);

        // Assert
        Assert.Equal(0xF8, _state.AL);
    }

    [Fact]
    public void TestSegmentRegister() {
        // Arrange
        SegmentRegisterNode segmentRegister = new SegmentRegisterNode((int)SegmentRegisterIndex.EsIndex);

        // Act
        ExecuteAssignment(segmentRegister, 0xF8);

        // Assert
        Assert.Equal(0xF8, _state.ES);
    }

    [Fact]
    public void TestMemoryAddressSegmentedFromRegs() {
        // Arrange
        _state.ES = 1;
        _state.DI = 2;
        RegisterNode offset = new RegisterNode(DataType.UINT16, (int)RegisterIndex.DiIndex);
        SegmentRegisterNode segmentRegister = new SegmentRegisterNode((int)SegmentRegisterIndex.EsIndex);
        SegmentedPointerNode pointerNode = new SegmentedPointerNode(DataType.UINT8, segmentRegister, null, offset);

        // Act
        ExecuteAssignment(pointerNode, 0xF8);

        // Assert
        Assert.Equal(0xF8, _memory.UInt8[1, 2]);
    }

    [Fact]
    public void TestAdd() {
        // Arrange
        _state.AX = 1;
        _state.BX = 2;
        RegisterNode operand1 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode operand2 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, operand1, BinaryOperation.PLUS, operand2);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(3, res);
    }

    [Fact]
    public void TestEquals() {
        // Arrange
        _state.AX = 1;
        RegisterNode operand1 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, operand1, BinaryOperation.EQUAL, operand2);

        // Act
        bool res = ToBool(operationNode);

        // Assert
        Assert.True(res);
    }

    [Fact]
    public void TestNotEquals() {
        // Arrange
        _state.AX = 1;
        RegisterNode operand1 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, operand1, BinaryOperation.NOT_EQUAL, operand2);

        // Act
        bool res = ToBool(operationNode);

        // Assert
        Assert.False(res);
    }

    [Fact]
    public void TestNot() {
        // Arrange
        _state.AX = 1;
        RegisterNode operand1 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, operand1, BinaryOperation.EQUAL, operand2);
        UnaryOperationNode unaryOperationNode = new UnaryOperationNode(DataType.BOOL, UnaryOperation.NOT, operationNode);

        // Act
        bool res = ToBool(unaryOperationNode);

        // Assert
        Assert.False(res);
    }

    private bool ToBool(IVisitableAstNode node) {
        Expression expression = node.Accept(_astExpressionBuilder);
        Func<State, Memory, bool> func = _astExpressionBuilder.ToFuncBool(expression).Compile();
        return func(_state, _memory);
    }

    private ushort ToUint16(IVisitableAstNode node) {
        Expression expression = node.Accept(_astExpressionBuilder);
        Func<State, Memory, ushort> func = _astExpressionBuilder.ToFuncUInt16(expression).Compile();
        return func(_state, _memory);
    }

    private void Execute(IVisitableAstNode node) {
        Expression expression = node.Accept(_astExpressionBuilder);
        Action<State, Memory> func = _astExpressionBuilder.ToAction(expression).Compile();
        func(_state, _memory);
    }

    private void ExecuteAssignment(ValueNode destination, byte value) {
        ConstantNode valueNode = new(destination.DataType, value);
        BinaryOperationNode operation = new(destination.DataType, destination, BinaryOperation.ASSIGN, valueNode);
        
        // Act
        Execute(operation);
    }
}