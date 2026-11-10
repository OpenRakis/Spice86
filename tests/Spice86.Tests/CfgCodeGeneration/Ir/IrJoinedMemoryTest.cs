namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using System.Collections.Generic;
using System.Linq;

using Xunit;

/// <summary>
/// <see cref="IrJoinedMemory"/> joins a word pair only when the address proof holds: same known
/// segment, constant adjacent offsets below the 64 KiB wrap, the whole four-byte range in known
/// RAM. Each refusal leaves the pair untouched.
/// </summary>
public sealed class IrJoinedMemoryTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);
    private static readonly SegmentRegisterNode Ds = new(3);

    private static readonly ConstantNode KnownSegment = new(DataType.UINT16, 0x2000);
    private static readonly ConstantNode VramSegment = new(DataType.UINT16, 0xA000);

    [Fact]
    public void JoinedLoad_VramSegment_Stays() {
        // Arrange: 0xA000:0x10 is device-mapped, not plain RAM.
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Ax, Pack(VramSegment, 0x10)));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void JoinedLoad_WrappedBoundary_Stays() {
        // Arrange: the two word reads at FFFEh and 0000h are each valid, but the wide read at
        // FFFEh would cross the 64 KiB segment limit instead of wrapping.
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Ax, new BinaryOperationNode(DataType.UINT32,
                new TypeConversionNode(DataType.UINT32, WordPointer(KnownSegment, 0xFFFE)),
                BinaryOperation.BITWISE_OR,
                new BinaryOperationNode(DataType.UINT32,
                    new TypeConversionNode(DataType.UINT32, WordPointer(KnownSegment, 0)),
                    BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, 16)))));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void JoinedLoad_WideOffsetExpression_Stays() {
        // Arrange: a 32-bit offset expression can carry a value a word offset never had.
        SegmentedPointerNode low = new(DataType.UINT16, KnownSegment, null,
            new TypeConversionNode(DataType.UINT32,
                new ConstantNode(DataType.UINT16, 0x10)));
        SegmentedPointerNode high = new(DataType.UINT16, KnownSegment, null,
            new ConstantNode(DataType.UINT16, 0x12));
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Ax, new BinaryOperationNode(DataType.UINT32,
                new TypeConversionNode(DataType.UINT32, low),
                BinaryOperation.BITWISE_OR,
                new BinaryOperationNode(DataType.UINT32,
                    new TypeConversionNode(DataType.UINT32, high),
                    BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, 16)))));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void JoinedLoad_UnknownSegment_Stays() {
        // Arrange: a segment register whose value is never established is never known RAM.
        (IrMethod method, IrBlock block) = SequenceMethod(
            Assign(Ax, Pack(Ds, 0x10)));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void JoinedStore_NonAdjacent_Stays() {
        // Arrange
        (IrMethod method, IrBlock block) = SequenceMethod(
            Store(KnownSegment, 0x10, Ax),
            Store(KnownSegment, 0x14, Bx));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void JoinedStore_WrappedBoundary_Stays() {
        // Arrange: word stores at FFFEh and 0000h; the wide store at FFFEh would cross the limit.
        (IrMethod method, IrBlock block) = SequenceMethod(
            Store(KnownSegment, 0xFFFE, Ax),
            Store(KnownSegment, 0x0000, Bx));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void JoinedStore_ObserverBetween_Stays() {
        // Arrange: an effective statement between the two stores keeps them non-consecutive.
        (IrMethod method, IrBlock block) = SequenceMethod(
            Store(KnownSegment, 0x10, Ax),
            Assign(Bx, new BinaryOperationNode(DataType.UINT16, Bx,
                BinaryOperation.PLUS, new ConstantNode(DataType.UINT16, 1))),
            Store(KnownSegment, 0x12, Ax));
        IrSsa ssa = IrSsa.Build(method);

        // Act
        int joined = IrJoinedMemory.Apply(method, ssa);

        // Assert
        joined.Should().Be(0);
        block.Statements.Should().HaveCount(4);
    }

    private static SegmentedPointerNode WordPointer(ValueNode segment, int offset) =>
        new(DataType.UINT16, segment, null, new ConstantNode(DataType.UINT16, (ulong)offset));

    private static BinaryOperationNode Pack(ValueNode segment, int lowOffset) =>
        new(DataType.UINT32,
            new TypeConversionNode(DataType.UINT32, WordPointer(segment, lowOffset)),
            BinaryOperation.BITWISE_OR,
            new BinaryOperationNode(DataType.UINT32,
                new TypeConversionNode(DataType.UINT32, WordPointer(segment, lowOffset + 2)),
                BinaryOperation.LEFT_SHIFT, new ConstantNode(DataType.INT32, 16)));

    private static BinaryOperationNode Assign(ValueNode left, ValueNode right) =>
        new(right.DataType, left, BinaryOperation.ASSIGN, right);

    private static BinaryOperationNode Store(ValueNode segment, int offset, ValueNode value) =>
        new(DataType.UINT16, WordPointer(segment, offset), BinaryOperation.ASSIGN, value);

    private static (IrMethod Method, IrBlock Block) SequenceMethod(
            params IVisitableAstNode[] nodes) {
        CfgInstruction entryInstruction = Instruction(0);
        IrBlock entry = new(0);
        IrBlock sequence = new(1);
        entry.Terminator = new IrJump(entryInstruction, new IrEdge(sequence, false));
        sequence.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"),
            Instruction(1)));
        foreach ((IVisitableAstNode node, int i) in nodes.Select((n, i) => (n, i))) {
            sequence.Statements.Add(IrStatement.Create(node, Instruction((ushort)(i + 2))));
        }
        sequence.Terminator = new IrExit(Instruction((ushort)(nodes.Length + 2)),
            new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = new("M", [entry, sequence],
            [new IrEntry(entryInstruction, entry)],
            new HashSet<ICfgNode> { entryInstruction },
            new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, sequence);
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));
}
