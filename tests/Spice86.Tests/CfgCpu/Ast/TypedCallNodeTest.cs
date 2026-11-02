namespace Spice86.Tests.CfgCpu.Ast;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// <see cref="MethodCallNode.WithArguments"/> and <see cref="MethodCallValueNode.WithArguments"/> are
/// virtual: every typed call node must rebuild itself, keeping its operation and width, so a pass that
/// rebuilds a call with new arguments never degrades it to a plain method call.
/// </summary>
public class TypedCallNodeTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly ConstantNode One = new(DataType.UINT16, 1);

    [Fact]
    public void AluCallNode_KeepsItsSubtype() {
        AluCallNode call = new(AluOperation.Add, BitWidth.WORD_16, Ax);

        MethodCallNode rebuilt = call.WithArguments([Ax, One]);

        rebuilt.Should().BeOfType<AluCallNode>().Which.Should().BeEquivalentTo(
            new AluCallNode(AluOperation.Add, BitWidth.WORD_16, Ax, One));
    }

    [Fact]
    public void AluCallValueNode_KeepsItsSubtype() {
        AluCallValueNode call = new(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16, Ax);

        MethodCallValueNode rebuilt = call.WithArguments([Ax, One]);

        rebuilt.Should().BeOfType<AluCallValueNode>().Which.Should().BeEquivalentTo(
            new AluCallValueNode(DataType.UINT16, AluOperation.Add, BitWidth.WORD_16, Ax, One));
    }

    [Fact]
    public void StackCallNode_KeepsItsSubtype() {
        StackCallNode call = new(StackOperation.Push, BitWidth.WORD_16, Ax);

        MethodCallNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<StackCallNode>().Which.Should().BeEquivalentTo(
            new StackCallNode(StackOperation.Push, BitWidth.WORD_16, One));
    }

    [Fact]
    public void StackCallValueNode_KeepsItsSubtype() {
        StackCallValueNode call = new(StackOperation.Pop, BitWidth.WORD_16);

        MethodCallValueNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<StackCallValueNode>().Which.Should().BeEquivalentTo(
            new StackCallValueNode(StackOperation.Pop, BitWidth.WORD_16, One));
    }

    [Fact]
    public void PortReadCallNode_KeepsItsSubtype() {
        PortReadCallNode call = new(BitWidth.BYTE_8, Ax);

        MethodCallValueNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<PortReadCallNode>().Which.Should().BeEquivalentTo(
            new PortReadCallNode(BitWidth.BYTE_8, One));
    }

    [Fact]
    public void PortWriteCallNode_KeepsItsSubtype() {
        PortWriteCallNode call = new(BitWidth.WORD_16, Ax, Ax);

        MethodCallNode rebuilt = call.WithArguments([Ax, One]);

        rebuilt.Should().BeOfType<PortWriteCallNode>().Which.Should().BeEquivalentTo(
            new PortWriteCallNode(BitWidth.WORD_16, Ax, One));
    }

    [Fact]
    public void BoundCheckCallNode_KeepsItsSubtype() {
        BoundCheckCallNode call = new(BitWidth.WORD_16, Ax);

        MethodCallNode rebuilt = call.WithArguments([Ax, One]);

        rebuilt.Should().BeOfType<BoundCheckCallNode>().Which.Should().BeEquivalentTo(
            new BoundCheckCallNode(BitWidth.WORD_16, Ax, One));
    }

    [Fact]
    public void BitScanCallNode_KeepsItsSubtype() {
        BitScanCallNode call = new(DataType.UINT16, BitScanDirection.Forward, Ax);

        MethodCallValueNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<BitScanCallNode>().Which.Should().BeEquivalentTo(
            new BitScanCallNode(DataType.UINT16, BitScanDirection.Forward, One));
    }

    [Fact]
    public void ProcedureCallHelperNode_KeepsItsSubtype() {
        ProcedureCallHelperNode call = new(CallDistance.Far, BitWidth.DWORD_32, Ax);

        MethodCallNode rebuilt = call.WithArguments([Ax, One]);

        rebuilt.Should().BeOfType<ProcedureCallHelperNode>().Which.Should().BeEquivalentTo(
            new ProcedureCallHelperNode(CallDistance.Far, BitWidth.DWORD_32, Ax, One));
    }

    [Fact]
    public void ProcedureReturnHelperNode_KeepsItsSubtype() {
        ProcedureReturnHelperNode call = new(CallDistance.Near, BitWidth.WORD_16, Ax);

        MethodCallNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<ProcedureReturnHelperNode>().Which.Should().BeEquivalentTo(
            new ProcedureReturnHelperNode(CallDistance.Near, BitWidth.WORD_16, One));
    }

    [Fact]
    public void HltHelperNode_KeepsItsSubtype() {
        HltHelperNode call = new();

        MethodCallNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<HltHelperNode>();
    }

    [Fact]
    public void InterruptCallHelperNode_KeepsItsSubtype() {
        InterruptCallHelperNode call = new(Ax);

        MethodCallNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<InterruptCallHelperNode>().Which.Should().BeEquivalentTo(
            new InterruptCallHelperNode(One));
    }

    [Fact]
    public void CallbackHelperNode_KeepsItsSubtype() {
        CallbackHelperNode call = new(Ax);

        MethodCallNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<CallbackHelperNode>().Which.Should().BeEquivalentTo(
            new CallbackHelperNode(One));
    }

    [Fact]
    public void CallbackMovedCsIpHelperNode_KeepsItsSubtype() {
        CallbackMovedCsIpHelperNode call = new(Ax);

        MethodCallValueNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<CallbackMovedCsIpHelperNode>().Which.Should().BeEquivalentTo(
            new CallbackMovedCsIpHelperNode(One));
    }

    [Fact]
    public void SignatureMatchesHelperNode_KeepsItsSubtype() {
        SignatureMatchesHelperNode call = new(Ax);

        MethodCallValueNode rebuilt = call.WithArguments([One]);

        rebuilt.Should().BeOfType<SignatureMatchesHelperNode>().Which.Should().BeEquivalentTo(
            new SignatureMatchesHelperNode(One));
    }
}
