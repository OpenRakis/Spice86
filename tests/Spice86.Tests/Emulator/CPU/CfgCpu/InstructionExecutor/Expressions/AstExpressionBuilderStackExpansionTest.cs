namespace Spice86.Tests.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using System.Linq.Expressions;

using Xunit;

/// <summary>
/// Executes every stack expansion on a machine and checks registers, memory and faults against the
/// <c>Stack</c> helper's semantics: SP wrap at 0, a #SS at the real-mode segment limit and the
/// partial state a mid-sequence PopAll fault leaves.
/// </summary>
public class AstExpressionBuilderStackExpansionTest {
    private readonly Memory _memory = new(new(), new Ram(0x20000), new(), new RealModeMmu386(), false);
    private readonly State _state = new(CpuModel.INTEL_80286);
    private static readonly RegisterNode Sp = new(DataType.UINT16, (int)RegisterIndex.SpIndex);

    private void Execute(IVisitableAstNode node) {
        AstExpressionBuilder builder = new(true);
        builder.ToAction(node.Accept(builder)).Compile()(_state, _memory);
    }

    private T Evaluate<T>(DataType type, IVisitableAstNode node) {
        AstExpressionBuilder builder = new(true);
        Expression expression = node.Accept(builder);
        Delegate compiled = type == DataType.UINT32
            ? builder.ToFuncUInt32(expression).Compile()
            : builder.ToFuncUInt16(expression).Compile();
        object result = compiled.DynamicInvoke(_state, _memory)
            ?? throw new InvalidOperationException("The compiled expression returned null.");
        return (T)result;
    }

    [Fact]
    public void StackCallIsAHelperCallWhenExpansionIsOff() {
        // Arrange
        AstExpressionBuilder builder = new(false);

        // Act
        Expression expression = new StackCallNode(StackOperation.Push, BitWidth.WORD_16,
            new ConstantNode(DataType.UINT16, 1)).Accept(builder);

        // Assert
        expression.Should().BeAssignableTo<MethodCallExpression>();
    }

    [Fact]
    public void PushAllStaysAHelperCallWhenExpansionIsOn() {
        // Arrange: the pending-fault ordering cannot be lowered by a leading check node
        AstExpressionBuilder builder = new(true);
        RegisterNode ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
        StackCallNode pushAll = new(StackOperation.PushAll, BitWidth.WORD_16,
            ax, ax, ax, ax, ax, ax, ax, ax);

        // Act
        Expression expression = pushAll.Accept(builder);

        // Assert
        expression.Should().BeAssignableTo<MethodCallExpression>();
    }

    [Fact]
    public void ExpandedPush16MatchesTheHelper() {
        // Arrange
        _state.SS = 0x1000;
        _state.SP = 0x0100;

        // Act
        Execute(new StackCallNode(StackOperation.Push, BitWidth.WORD_16,
            new ConstantNode(DataType.UINT16, 0xBEEF)));

        // Assert
        _state.SP.Should().Be(0x00FE);
        _memory.UInt16[0x1000, 0x00FE, SegmentAccessKind.Stack].Should().Be(0xBEEF);
    }

    [Fact]
    public void ExpandedPush32KeepsTheEspHighHalf() {
        // Arrange
        _state.SS = 0x1000;
        _state.ESP = 0x12340200;

        // Act
        Execute(new StackCallNode(StackOperation.Push, BitWidth.DWORD_32,
            new ConstantNode(DataType.UINT32, 0xDEADBEEF)));

        // Assert
        _state.ESP.Should().Be(0x123401FC, "a 32-bit push writes the low 16 bits of ESP only");
        _memory.UInt32[0x1000, 0x01FC, SegmentAccessKind.Stack].Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void ExpandedPushSpPushesTheOldValue() {
        // Arrange: the operand is bound before the new offset is computed
        _state.SS = 0x1000;
        _state.SP = 0x0100;

        // Act
        Execute(new StackCallNode(StackOperation.Push, BitWidth.WORD_16, Sp));

        // Assert
        _state.SP.Should().Be(0x00FE);
        _memory.UInt16[0x1000, 0x00FE, SegmentAccessKind.Stack].Should().Be(0x0100);
    }

    [Fact]
    public void ExpandedPush16WrapsAtZero() {
        // Arrange: SP = 0 gives newSp = 0xFFFE whose 2-byte store just fits the limit
        _state.SS = 0x1000;
        _state.SP = 0x0000;

        // Act
        Execute(new StackCallNode(StackOperation.Push, BitWidth.WORD_16,
            new ConstantNode(DataType.UINT16, 0x1234)));

        // Assert
        _state.SP.Should().Be(0xFFFE);
        _memory.UInt16[0x1000, 0xFFFE, SegmentAccessKind.Stack].Should().Be(0x1234);
    }

    [Fact]
    public void ExpandedPush16FaultsPastTheSegmentLimit() {
        // Arrange: SP = 1 gives newSp = 0xFFFF whose 2-byte store crosses the segment limit
        _state.SS = 0x1000;
        _state.SP = 0x0001;

        // Act
        Action act = () => Execute(new StackCallNode(StackOperation.Push, BitWidth.WORD_16,
            new ConstantNode(DataType.UINT16, 0x1234)));

        // Assert: the store faults before SP moves, like Stack.Push16
        act.Should().Throw<CpuStackSegmentFaultException>();
        _state.SP.Should().Be(0x0001);
    }

    [Fact]
    public void ExpandedPop32KeepsTheEspHighHalf() {
        // Arrange
        _state.SS = 0x1000;
        _state.ESP = 0x12340100;
        _memory.UInt32[0x1000, 0x0100, SegmentAccessKind.Stack] = 0xA5A5A5A5;

        // Act
        uint popped = Evaluate<uint>(DataType.UINT32,
            new StackCallValueNode(StackOperation.Pop, BitWidth.DWORD_32));

        // Assert
        popped.Should().Be(0xA5A5A5A5);
        _state.ESP.Should().Be(0x12340104);
    }

    [Fact]
    public void ExpandedPopSpEndsWithTheLoadedValue() {
        // Arrange: the pop reads, advances SP, then the enclosing assignment writes the loaded value
        _state.SS = 0x1000;
        _state.SP = 0x0100;
        _memory.UInt16[0x1000, 0x0100, SegmentAccessKind.Stack] = 0x7777;
        BinaryOperationNode program = new(DataType.UINT16, Sp, BinaryOperation.ASSIGN,
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16));

        // Act
        Execute(program);

        // Assert
        _state.SP.Should().Be(0x7777);
    }

    [Fact]
    public void ExpandedPeekReadsWithoutMovingSp() {
        // Arrange
        _state.SS = 0x1000;
        _state.SP = 0x0100;
        _memory.UInt16[0x1000, 0x0104, SegmentAccessKind.Stack] = 0xABCD;

        // Act
        ushort value = Evaluate<ushort>(DataType.UINT16,
            new StackCallValueNode(StackOperation.Peek, BitWidth.WORD_16,
                new ConstantNode(DataType.UINT32, 4)));

        // Assert
        value.Should().Be(0xABCD);
        _state.SP.Should().Be(0x0100);
    }

    [Fact]
    public void ExpandedLeave16KeepsTheEbpHighHalf() {
        // Arrange
        _state.SS = 0x1000;
        _state.EBP = 0xFFFF0200;
        _state.SP = 0x0100;
        _memory.UInt16[0x1000, 0x0200, SegmentAccessKind.Stack] = 0x4321;

        // Act
        Execute(new StackCallNode(StackOperation.Leave, BitWidth.WORD_16));

        // Assert
        _state.SP.Should().Be(0x0202);
        _state.EBP.Should().Be(0xFFFF4321, "leave16 writes BP only");
    }

    [Fact]
    public void ExpandedLeave32ReadsBpAndWritesFullEbp() {
        // Arrange: the frame address is BP, not EBP
        _state.SS = 0x1000;
        _state.EBP = 0xDEAD0200;
        _state.SP = 0x0100;
        _memory.UInt32[0x1000, 0x0200, SegmentAccessKind.Stack] = 0xCAFEBABE;

        // Act
        Execute(new StackCallNode(StackOperation.Leave, BitWidth.DWORD_32));

        // Assert
        _state.SP.Should().Be(0x0204);
        _state.EBP.Should().Be(0xCAFEBABE);
    }

    [Fact]
    public void ExpandedPopAll16PopsInHelperOrder() {
        // Arrange
        _state.SS = 0x1000;
        _state.ESP = 0x55550100;
        for (int i = 0; i < 8; i++) {
            _memory.UInt16[0x1000, (ushort)(0x0100 + 2 * i), SegmentAccessKind.Stack] = (ushort)(0x1000 + i);
        }

        // Act
        Execute(new StackCallNode(StackOperation.PopAll, BitWidth.WORD_16));

        // Assert: POPA order DI, SI, BP, skip SP, BX, DX, CX, AX
        _state.DI.Should().Be(0x1000);
        _state.SI.Should().Be(0x1001);
        _state.BP.Should().Be(0x1002);
        _state.BX.Should().Be(0x1004);
        _state.DX.Should().Be(0x1005);
        _state.CX.Should().Be(0x1006);
        _state.AX.Should().Be(0x1007);
        _state.ESP.Should().Be(0x55550110, "popa writes SP only");
    }

    [Fact]
    public void ExpandedPopAll16FaultLeavesTheCommittedRegisters() {
        // Arrange: SP = 0xFFF7 puts the DI, SI and BP loads and the SP-slot check within the limit
        // but the BX load at offset 0xFFFF past it: earlier commits persist, SP stays
        _state.SS = 0x1000;
        _state.SP = 0xFFF7;
        _state.DI = _state.SI = _state.BP = _state.BX = _state.DX = _state.CX = _state.AX = 0;
        _memory.UInt16[0x1000, 0xFFF7, SegmentAccessKind.Stack] = 0x00D1;
        _memory.UInt16[0x1000, 0xFFF9, SegmentAccessKind.Stack] = 0x00D2;
        _memory.UInt16[0x1000, 0xFFFB, SegmentAccessKind.Stack] = 0x00D3;

        // Act
        Action act = () => Execute(new StackCallNode(StackOperation.PopAll, BitWidth.WORD_16));

        // Assert
        act.Should().Throw<CpuStackSegmentFaultException>();
        _state.DI.Should().Be(0x00D1);
        _state.SI.Should().Be(0x00D2);
        _state.BP.Should().Be(0x00D3);
        _state.BX.Should().Be(0);
        _state.AX.Should().Be(0);
        _state.SP.Should().Be(0xFFF7, "SP is left unchanged on a mid-sequence fault");
    }

    [Fact]
    public void ExpandedPopAll16FaultsOnTheSpSlotCheck() {
        // Arrange: SP = 0xFFF9 lands the SP-slot check at offset 0xFFFF, past the limit
        _state.SS = 0x1000;
        _state.SP = 0xFFF9;
        _state.DI = _state.SI = _state.BP = _state.BX = _state.DX = _state.CX = _state.AX = 0;
        _memory.UInt16[0x1000, 0xFFF9, SegmentAccessKind.Stack] = 0x00D1;
        _memory.UInt16[0x1000, 0xFFFB, SegmentAccessKind.Stack] = 0x00D2;
        _memory.UInt16[0x1000, 0xFFFD, SegmentAccessKind.Stack] = 0x00D3;

        // Act
        Action act = () => Execute(new StackCallNode(StackOperation.PopAll, BitWidth.WORD_16));

        // Assert: the check faults without committing anything after BP
        act.Should().Throw<CpuStackSegmentFaultException>();
        _state.DI.Should().Be(0x00D1);
        _state.SI.Should().Be(0x00D2);
        _state.BP.Should().Be(0x00D3);
        _state.BX.Should().Be(0);
        _state.SP.Should().Be(0xFFF9);
    }

    [Fact]
    public void ExpandedPopAll32MergesTheEspSlotHighHalf() {
        // Arrange
        _state.SS = 0x1000;
        _state.SP = 0x0100;
        for (int i = 0; i < 8; i++) {
            _memory.UInt32[0x1000, (ushort)(0x0100 + 4 * i), SegmentAccessKind.Stack] = (uint)(0x10000 + i);
        }
        _memory.UInt32[0x1000, 0x010C, SegmentAccessKind.Stack] = 0xABCD0000;

        // Act
        Execute(new StackCallNode(StackOperation.PopAll, BitWidth.DWORD_32));

        // Assert
        _state.EDI.Should().Be(0x10000);
        _state.ESI.Should().Be(0x10001);
        _state.EBP.Should().Be(0x10002);
        _state.EBX.Should().Be(0x10004);
        _state.EDX.Should().Be(0x10005);
        _state.ECX.Should().Be(0x10006);
        _state.EAX.Should().Be(0x10007);
        _state.ESP.Should().Be(0xABCD0120, "ESP keeps the slot's high half and takes the new SP low half");
    }
}
