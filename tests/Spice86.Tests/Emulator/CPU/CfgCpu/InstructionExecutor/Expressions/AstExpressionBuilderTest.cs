using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

namespace Spice86.Tests.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

using System.Linq.Expressions;

using Xunit;

public class AstExpressionBuilderTest {
    private readonly Memory _memory = new(new(), new Ram(64), new());
    private readonly State _state = new(CpuModel.INTEL_80286);
    private readonly AstExpressionBuilder _astExpressionBuilder = new();

    // ── Memory access tests ──

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
    public void TestSegmentedPointerWithNonUshortExpressions() {
        // Arrange: use UINT32 constants as segment/offset — should auto-convert to ushort
        ConstantNode segment = new ConstantNode(DataType.UINT32, 1);
        ConstantNode offset = new ConstantNode(DataType.UINT32, 2);
        SegmentedPointerNode pointerNode = new SegmentedPointerNode(DataType.UINT8, segment, null, offset);

        // Act
        ExecuteAssignment(pointerNode, 0xAB);

        // Assert
        Assert.Equal(0xAB, _memory.UInt8[1, 2]);
    }

    // ── Register tests ──

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

    // ── Arithmetic tests ──

    [Fact]
    public void TestAdd() {
        // Arrange: result goes into AX via ASSIGN, so promotion is transparent
        _state.AX = 0;
        _state.BX = 2;
        RegisterNode ax = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode operand1 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode operand2 = new RegisterNode(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode addNode = new BinaryOperationNode(DataType.UINT16, operand1, BinaryOperation.PLUS, operand2);

        // AX = 1
        ConstantNode one = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode assignAx = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.ASSIGN, one);

        // AX = AX + BX
        BinaryOperationNode assignResult = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.ASSIGN, addNode);

        BlockNode block = new BlockNode(assignAx, assignResult);

        // Act
        Execute(block);

        // Assert
        Assert.Equal(3, _state.AX);
    }

    [Fact]
    public void TestSubtract() {
        // Arrange
        _state.AX = 5;
        _state.BX = 3;
        RegisterNode ax = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        RegisterNode bx = new RegisterNode(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode subNode = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.MINUS, bx);
        RegisterNode cx = new RegisterNode(DataType.UINT16, (int)RegisterIndex.CxIndex);
        BinaryOperationNode assignResult = new BinaryOperationNode(DataType.UINT16, cx, BinaryOperation.ASSIGN, subNode);

        // Act
        Execute(assignResult);

        // Assert
        Assert.Equal(2, _state.CX);
    }

    [Fact]
    public void TestAddMixedTypes_BytePlusUint16_PromotesToInt() {
        // Arrange: byte(10) + ushort(20) — both promote to int for arithmetic
        ConstantNode byteVal = new ConstantNode(DataType.UINT8, 10);
        ConstantNode ushortVal = new ConstantNode(DataType.UINT16, 20);
        BinaryOperationNode addNode = new BinaryOperationNode(DataType.INT32, byteVal, BinaryOperation.PLUS, ushortVal);

        // Act
        Expression expression = addNode.Accept(_astExpressionBuilder);
        Func<State, Memory, int> func = _astExpressionBuilder.ToFuncInt32(expression).Compile();
        int result = func(_state, _memory);

        // Assert
        Assert.Equal(30, result);
    }

    [Fact]
    public void TestMultiply() {
        // Arrange: store result via ASSIGN since arithmetic promotes ushort to int
        _state.AX = 0;
        RegisterNode ax = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand1 = new ConstantNode(DataType.UINT16, 6);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 7);
        BinaryOperationNode mulNode = new BinaryOperationNode(DataType.UINT16, operand1, BinaryOperation.MULTIPLY, operand2);
        BinaryOperationNode assignResult = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.ASSIGN, mulNode);

        // Act
        Execute(assignResult);

        // Assert
        Assert.Equal(42, _state.AX);
    }

    [Fact]
    public void TestDivide() {
        // Arrange: store result via ASSIGN since arithmetic promotes ushort to int
        _state.AX = 0;
        RegisterNode ax = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand1 = new ConstantNode(DataType.UINT16, 42);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 7);
        BinaryOperationNode divNode = new BinaryOperationNode(DataType.UINT16, operand1, BinaryOperation.DIVIDE, operand2);
        BinaryOperationNode assignResult = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.ASSIGN, divNode);

        // Act
        Execute(assignResult);

        // Assert
        Assert.Equal(6, _state.AX);
    }

    [Fact]
    public void TestModulo() {
        // Arrange: store result via ASSIGN since arithmetic promotes ushort to int
        _state.AX = 0;
        RegisterNode ax = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode operand1 = new ConstantNode(DataType.UINT16, 10);
        ConstantNode operand2 = new ConstantNode(DataType.UINT16, 3);
        BinaryOperationNode modNode = new BinaryOperationNode(DataType.UINT16, operand1, BinaryOperation.MODULO, operand2);
        BinaryOperationNode assignResult = new BinaryOperationNode(DataType.UINT16, ax, BinaryOperation.ASSIGN, modNode);

        // Act
        Execute(assignResult);

        // Assert
        Assert.Equal(1, _state.AX);
    }

    // ── Comparison tests ──

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
    public void TestLessThan() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 3);
        ConstantNode right = new ConstantNode(DataType.UINT16, 5);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LESS_THAN, right);

        // Act & Assert
        Assert.True(ToBool(operationNode));
    }

    [Fact]
    public void TestGreaterThan() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 5);
        ConstantNode right = new ConstantNode(DataType.UINT16, 3);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.GREATER_THAN, right);

        // Act & Assert
        Assert.True(ToBool(operationNode));
    }

    [Fact]
    public void TestLessThanOrEqual() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 5);
        ConstantNode right = new ConstantNode(DataType.UINT16, 5);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.LESS_THAN_OR_EQUAL, right);

        // Act & Assert
        Assert.True(ToBool(operationNode));
    }

    [Fact]
    public void TestGreaterThanOrEqual() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 5);
        ConstantNode right = new ConstantNode(DataType.UINT16, 3);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, left, BinaryOperation.GREATER_THAN_OR_EQUAL, right);

        // Act & Assert
        Assert.True(ToBool(operationNode));
    }

    // ── Logical tests ──

    [Fact]
    public void TestLogicalAnd() {
        // Arrange
        ConstantNode trueNode = new ConstantNode(DataType.BOOL, 1);
        ConstantNode falseNode = new ConstantNode(DataType.BOOL, 0);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, trueNode, BinaryOperation.LOGICAL_AND, falseNode);

        // Act & Assert
        Assert.False(ToBool(operationNode));
    }

    [Fact]
    public void TestLogicalOr() {
        // Arrange
        ConstantNode trueNode = new ConstantNode(DataType.BOOL, 1);
        ConstantNode falseNode = new ConstantNode(DataType.BOOL, 0);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.BOOL, trueNode, BinaryOperation.LOGICAL_OR, falseNode);

        // Act & Assert
        Assert.True(ToBool(operationNode));
    }

    // ── Bitwise tests ──

    [Fact]
    public void TestBitwiseAnd() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 0xFF);
        ConstantNode right = new ConstantNode(DataType.UINT16, 0x0F);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, left, BinaryOperation.BITWISE_AND, right);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(0x0F, res);
    }

    [Fact]
    public void TestBitwiseOr() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 0xF0);
        ConstantNode right = new ConstantNode(DataType.UINT16, 0x0F);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, left, BinaryOperation.BITWISE_OR, right);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(0xFF, res);
    }

    [Fact]
    public void TestBitwiseXor() {
        // Arrange
        ConstantNode left = new ConstantNode(DataType.UINT16, 0xFF);
        ConstantNode right = new ConstantNode(DataType.UINT16, 0x0F);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, left, BinaryOperation.BITWISE_XOR, right);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(0xF0, res);
    }

    // ── Shift tests ──

    [Fact]
    public void TestLeftShift() {
        // Arrange
        ConstantNode value = new ConstantNode(DataType.UINT16, 1);
        ConstantNode shiftAmount = new ConstantNode(DataType.UINT8, 4);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, value, BinaryOperation.LEFT_SHIFT, shiftAmount);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(16, res);
    }

    [Fact]
    public void TestRightShift() {
        // Arrange
        ConstantNode value = new ConstantNode(DataType.UINT16, 16);
        ConstantNode shiftAmount = new ConstantNode(DataType.UINT8, 4);
        BinaryOperationNode operationNode = new BinaryOperationNode(DataType.UINT16, value, BinaryOperation.RIGHT_SHIFT, shiftAmount);

        // Act
        ushort res = ToUint16(operationNode);

        // Assert
        Assert.Equal(1, res);
    }

    // ── Unary operation tests ──

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

    [Fact]
    public void TestNegate() {
        // Arrange
        ConstantNode operand = new ConstantNode(DataType.INT16, 5);
        UnaryOperationNode negateNode = new UnaryOperationNode(DataType.INT16, UnaryOperation.NEGATE, operand);

        // Act
        Expression expression = negateNode.Accept(_astExpressionBuilder);
        Func<State, Memory, short> func = _astExpressionBuilder.ToFuncInt16(expression).Compile();
        short result = func(_state, _memory);

        // Assert
        Assert.Equal(-5, result);
    }

    [Fact]
    public void TestBitwiseNot() {
        // Arrange
        ConstantNode operand = new ConstantNode(DataType.UINT16, 0xFF00);
        UnaryOperationNode bitwiseNotNode = new UnaryOperationNode(DataType.UINT16, UnaryOperation.BITWISE_NOT, operand);

        // Act
        ushort res = ToUint16(bitwiseNotNode);

        // Assert
        Assert.Equal(0x00FF, res);
    }

    // ── Assign with type mismatch ──

    [Fact]
    public void TestAssignWithTypeMismatch_Uint32ToUint16() {
        // Arrange: assign a UINT32 constant to a UINT16 register — should auto-convert
        _state.AX = 0;
        RegisterNode destination = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        ConstantNode value = new ConstantNode(DataType.UINT32, 42);
        BinaryOperationNode assignNode = new BinaryOperationNode(DataType.UINT16, destination, BinaryOperation.ASSIGN, value);

        // Act
        Execute(assignNode);

        // Assert
        Assert.Equal(42, _state.AX);
    }

    // ── Constant node tests ──

    [Fact]
    public void TestConstantNodeBool_True() {
        // Arrange
        ConstantNode trueConst = new ConstantNode(DataType.BOOL, 1);

        // Act & Assert
        Assert.True(ToBool(trueConst));
    }

    [Fact]
    public void TestConstantNodeBool_False() {
        // Arrange
        ConstantNode falseConst = new ConstantNode(DataType.BOOL, 0);

        // Act & Assert
        Assert.False(ToBool(falseConst));
    }

    [Fact]
    public void TestConstantNodeSigned_Int8() {
        // Arrange: -1 stored as 0xFF for INT8
        ConstantNode signedConst = new ConstantNode(DataType.INT8, 0xFF);

        // Act
        Expression expression = signedConst.Accept(_astExpressionBuilder);
        Func<State, Memory, sbyte> func = _astExpressionBuilder.ToFuncInt8(expression).Compile();
        sbyte result = func(_state, _memory);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public void TestConstantNodeSigned_Int16() {
        // Arrange: -1 stored as 0xFFFF for INT16
        ConstantNode signedConst = new ConstantNode(DataType.INT16, 0xFFFF);

        // Act
        Expression expression = signedConst.Accept(_astExpressionBuilder);
        Func<State, Memory, short> func = _astExpressionBuilder.ToFuncInt16(expression).Compile();
        short result = func(_state, _memory);

        // Assert
        Assert.Equal(-1, result);
    }

    // ── Type conversion test ──

    [Fact]
    public void TestTypeConversion_Uint8ToUint16() {
        // Arrange
        ConstantNode byteConst = new ConstantNode(DataType.UINT8, 200);
        TypeConversionNode conversionNode = new TypeConversionNode(DataType.UINT16, byteConst);

        // Act
        ushort res = ToUint16(conversionNode);

        // Assert
        Assert.Equal(200, res);
    }

    [Fact]
    public void TestTypeConversion_Int8ToInt16_SignExtends() {
        // Arrange: -5 as INT8
        ConstantNode signedByte = new ConstantNode(DataType.INT8, unchecked((ulong)(byte)-5));
        TypeConversionNode conversionNode = new TypeConversionNode(DataType.INT16, signedByte);

        // Act
        Expression expression = conversionNode.Accept(_astExpressionBuilder);
        Func<State, Memory, short> func = _astExpressionBuilder.ToFuncInt16(expression).Compile();
        short result = func(_state, _memory);

        // Assert
        Assert.Equal(-5, result);
    }

    // ── CPU flag tests ──

    [Fact]
    public void TestCpuFlagNode_CarryFlag() {
        // Arrange
        _state.CarryFlag = true;
        CpuFlagNode carryFlag = new CpuFlagNode(Flags.Carry);

        // Act & Assert
        Assert.True(ToBool(carryFlag));
    }

    [Fact]
    public void TestCpuFlagNode_AssignCarryFlag() {
        // Arrange
        _state.CarryFlag = false;
        CpuFlagNode carryFlag = new CpuFlagNode(Flags.Carry);
        ConstantNode trueConst = new ConstantNode(DataType.BOOL, 1);
        BinaryOperationNode assignNode = new BinaryOperationNode(DataType.BOOL, carryFlag, BinaryOperation.ASSIGN, trueConst);

        // Act
        Execute(assignNode);

        // Assert
        Assert.True(_state.CarryFlag);
    }

    [Fact]
    public void TestCpuFlagNode_ZeroFlag() {
        // Arrange
        _state.ZeroFlag = true;
        CpuFlagNode zeroFlag = new CpuFlagNode(Flags.Zero);

        // Act & Assert
        Assert.True(ToBool(zeroFlag));
    }

    // ── FlagRegister test ──

    [Fact]
    public void TestFlagRegisterNode() {
        // Arrange
        _state.Flags.FlagRegister = 0x0202; // bit 1 (reserved) + bit 9 (IF)
        FlagRegisterNode flagRegNode = new FlagRegisterNode(DataType.UINT32);

        // Act
        Expression expression = flagRegNode.Accept(_astExpressionBuilder);
        Func<State, Memory, uint> func = _astExpressionBuilder.ToFuncUInt32(expression).Compile();
        uint result = func(_state, _memory);

        // Assert
        Assert.Equal(0x0202u, result);
    }

    // ── SegmentedAddressValueNode test ──

    [Fact]
    public void TestSegmentedAddressValueNode() {
        // Arrange
        ConstantNode segment = new ConstantNode(DataType.UINT16, 0x1000);
        ConstantNode offset = new ConstantNode(DataType.UINT16, 0x0020);
        SegmentedAddressValueNode addressNode = new SegmentedAddressValueNode(segment, offset);

        // Act
        Expression expression = addressNode.Accept(_astExpressionBuilder);
        // The result should be a SegmentedAddress
        Func<State, Memory, SegmentedAddress> func =
            Expression.Lambda<Func<State, Memory, SegmentedAddress>>(expression, GetParameters()).Compile();
        SegmentedAddress result = func(_state, _memory);

        // Assert
        Assert.Equal(0x1000, result.Segment);
        Assert.Equal(0x0020, result.Offset);
    }

    // ── Variable and block tests ──

    [Fact]
    public void TestVariableDeclarationAndReference() {
        // Arrange: declare uint16 x = 42, then assign AX = x
        ConstantNode initValue = new ConstantNode(DataType.UINT16, 42);
        VariableDeclarationNode varDecl = new VariableDeclarationNode(DataType.UINT16, "x", initValue);

        RegisterNode axReg = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        VariableReferenceNode varRef = new VariableReferenceNode(DataType.UINT16, "x");
        BinaryOperationNode assignAx = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, varRef);

        BlockNode block = new BlockNode(varDecl, assignAx);

        // Act
        _state.AX = 0;
        Execute(block);

        // Assert
        Assert.Equal(42, _state.AX);
    }

    [Fact]
    public void TestVariableDeclarationWithTypeMismatch() {
        // Arrange: declare uint16 x = uint32(100) — should auto-convert
        ConstantNode initValue = new ConstantNode(DataType.UINT32, 100);
        VariableDeclarationNode varDecl = new VariableDeclarationNode(DataType.UINT16, "x", initValue);

        RegisterNode axReg = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        VariableReferenceNode varRef = new VariableReferenceNode(DataType.UINT16, "x");
        BinaryOperationNode assignAx = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, varRef);

        BlockNode block = new BlockNode(varDecl, assignAx);

        // Act
        _state.AX = 0;
        Execute(block);

        // Assert
        Assert.Equal(100, _state.AX);
    }

    // ── IfElse test ──

    [Fact]
    public void TestIfElse_TrueBranch() {
        // Arrange: if (true) { AX = 1 } else { AX = 2 }
        ConstantNode condition = new ConstantNode(DataType.BOOL, 1);
        RegisterNode axReg = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);

        ConstantNode one = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode assignOne = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, one);

        ConstantNode two = new ConstantNode(DataType.UINT16, 2);
        BinaryOperationNode assignTwo = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, two);

        IfElseNode ifElseNode = new IfElseNode(condition, assignOne, assignTwo);

        // Act
        _state.AX = 0;
        Execute(ifElseNode);

        // Assert
        Assert.Equal(1, _state.AX);
    }

    [Fact]
    public void TestIfElse_FalseBranch() {
        // Arrange: if (false) { AX = 1 } else { AX = 2 }
        ConstantNode condition = new ConstantNode(DataType.BOOL, 0);
        RegisterNode axReg = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);

        ConstantNode one = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode assignOne = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, one);

        ConstantNode two = new ConstantNode(DataType.UINT16, 2);
        BinaryOperationNode assignTwo = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, two);

        IfElseNode ifElseNode = new IfElseNode(condition, assignOne, assignTwo);

        // Act
        _state.AX = 0;
        Execute(ifElseNode);

        // Assert
        Assert.Equal(2, _state.AX);
    }

    // ── While loop test ──

    [Fact]
    public void TestWhileLoop() {
        // Arrange: uint16 x = 0; while (x < 5) { x = x + 1; }; AX = x
        ConstantNode initValue = new ConstantNode(DataType.UINT16, 0);
        VariableDeclarationNode varDecl = new VariableDeclarationNode(DataType.UINT16, "x", initValue);

        VariableReferenceNode xRef = new VariableReferenceNode(DataType.UINT16, "x");
        ConstantNode five = new ConstantNode(DataType.UINT16, 5);
        BinaryOperationNode condition = new BinaryOperationNode(DataType.BOOL, xRef, BinaryOperation.LESS_THAN, five);

        // body: x = x + 1
        ConstantNode one = new ConstantNode(DataType.UINT16, 1);
        BinaryOperationNode addOne = new BinaryOperationNode(DataType.UINT16, xRef, BinaryOperation.PLUS, one);
        BinaryOperationNode assignXPlusOne = new BinaryOperationNode(DataType.UINT16, xRef, BinaryOperation.ASSIGN, addOne);
        BlockNode body = new BlockNode(assignXPlusOne);

        WhileNode whileNode = new WhileNode(condition, body);

        // After loop: AX = x
        RegisterNode axReg = new RegisterNode(DataType.UINT16, (int)RegisterIndex.AxIndex);
        BinaryOperationNode assignAx = new BinaryOperationNode(DataType.UINT16, axReg, BinaryOperation.ASSIGN, xRef);

        BlockNode outerBlock = new BlockNode(varDecl, whileNode, assignAx);

        // Act
        _state.AX = 0;
        Execute(outerBlock);

        // Assert
        Assert.Equal(5, _state.AX);
    }

    // ── Helpers ──

    private ParameterExpression[] GetParameters() {
        // Access the parameters via reflection for custom lambda construction
        Expression dummyExpr = new ConstantNode(DataType.UINT16, 0).Accept(_astExpressionBuilder);
        Expression<Func<State, Memory, ushort>> lambda = _astExpressionBuilder.ToFuncUInt16(dummyExpr);
        return lambda.Parameters.ToArray();
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