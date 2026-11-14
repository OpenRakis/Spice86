namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// The <see cref="StackRuns"/> refusals no fixture reaches: a run inside a fault-observed region, a
/// pop run longer than six and a pop run assigning one register twice keep their separate
/// statements.
/// </summary>
public sealed class StackRunsTest {
    private static RegisterNode Reg(RegisterIndex index) =>
        new(DataType.UINT16, (int)index);

    private static StackCallNode Push(RegisterIndex index) =>
        new(StackOperation.Push, BitWidth.WORD_16, Reg(index));

    private static BinaryOperationNode PopAssign(RegisterIndex index) =>
        new(DataType.UINT16, Reg(index), BinaryOperation.ASSIGN,
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16));

    private static ReturnNode Ret() =>
        new(new MethodCallNode(null, "NearRet"));

    [Fact]
    public void PushRunInsideAFaultRegionStays() {
        // Arrange: two entry pushes wrapped in a CpuFaultTryCatchNode - a fault between them
        // observes SP, so the run must not merge.
        BlockNode faultBody = new(Push(RegisterIndex.BxIndex), Push(RegisterIndex.SiIndex));
        BlockNode body = new(new CpuFaultTryCatchNode(faultBody,
            new SegmentedAddress(0xF000, 0x0100), [], []));

        // Act
        BlockNode result = StackRuns.Apply(body);

        // Assert
        IrAstChildren.Descendants(result).OfType<StackSaveNode>().Should().BeEmpty();
        IrAstChildren.Descendants(result).OfType<StackCallNode>()
            .Where(call => call.Operation == StackOperation.Push).Should().HaveCount(2);
    }

    [Fact]
    public void PopRunLongerThanSixStays() {
        // Arrange: seven pops before the return, one register more than Restore6 covers.
        BlockNode body = new(
            PopAssign(RegisterIndex.AxIndex), PopAssign(RegisterIndex.CxIndex),
            PopAssign(RegisterIndex.DxIndex), PopAssign(RegisterIndex.BxIndex),
            PopAssign(RegisterIndex.BpIndex), PopAssign(RegisterIndex.SiIndex),
            PopAssign(RegisterIndex.DiIndex),
            Ret());

        // Act
        BlockNode result = StackRuns.Apply(body);

        // Assert
        IrAstChildren.Descendants(result).OfType<StackRestoreNode>().Should().BeEmpty();
        IrAstChildren.Descendants(result).OfType<BinaryOperationNode>()
            .Should().HaveCount(7);
    }

    [Fact]
    public void PopRunWithARepeatedTargetStays() {
        // Arrange: two pops into the same register - merging would drop the first assignment.
        BlockNode body = new(
            PopAssign(RegisterIndex.DiIndex), PopAssign(RegisterIndex.DiIndex), Ret());

        // Act
        BlockNode result = StackRuns.Apply(body);

        // Assert
        IrAstChildren.Descendants(result).OfType<StackRestoreNode>().Should().BeEmpty();
        IrAstChildren.Descendants(result).OfType<BinaryOperationNode>()
            .Should().HaveCount(2);
    }
}
