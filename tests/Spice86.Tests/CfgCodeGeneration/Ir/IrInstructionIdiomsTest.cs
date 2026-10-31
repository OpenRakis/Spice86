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
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The <c>xchg</c> and far-pointer idioms rewrite only on their tagged origins; each legality
/// refusal leaves the statements untouched.
/// </summary>
public sealed class IrInstructionIdiomsTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);
    private static readonly SegmentRegisterNode Ds = new(3);
    private static readonly SegmentRegisterNode Es = new(0);

    [Fact]
    public void ExchangeTriple_BecomesTupleSwap() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Bx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeEquivalentTo(new TupleSwapNode(Ax, Bx));
    }

    [Fact]
    public void ExchangeTriple_UntaggedOrigin_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Bx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.None, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
    }

    [Fact]
    public void ExchangeOfSameRegister_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Ax), Assign(Ax, Reference("tmp")));
        IrMethod method = Method(instruction, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void FarPointerQuadruple_BecomesTwoAssignments() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        ValueNode valueRead = Pointer(new ConstantNode(DataType.UINT16, 0x10));
        ValueNode segmentRead = Pointer(new ConstantNode(DataType.UINT16, 0x12));
        IrBlock block = Block(instruction, Declaration("value", valueRead),
            Declaration("segment", segmentRead),
            Assign(Bx, Reference("value")), Assign(Ds, Reference("segment")));
        IrMethod method = Method(instruction, IrInstructionShape.LoadFarPointer, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeEquivalentTo(Assign(Bx, valueRead));
        block.Statements[2].Node.Should().BeEquivalentTo(Assign(Ds, segmentRead));
    }

    [Fact]
    public void FarPointerSegmentAddressReadsValueRegister_Stays() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction,
            Declaration("value", Pointer(new ConstantNode(DataType.UINT16, 0x10))),
            Declaration("segment", Pointer(Bx)),
            Assign(Bx, Reference("value")), Assign(Ds, Reference("segment")));
        IrMethod method = Method(instruction, IrInstructionShape.LoadFarPointer, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(5);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void ExchangeTripleSplitAcrossOrigins_Stays() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock block = Block(first, Declaration("tmp", Ax), Assign(Ax, Bx));
        block.Statements.Add(IrStatement.Create(Assign(Bx, Reference("tmp")), second));
        IrMethod method = Method(first, IrInstructionShape.Exchange, block);

        // Act
        IrInstructionIdioms.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static SegmentedPointerNode Pointer(ValueNode offset) =>
        new(DataType.UINT16, Es, null, offset);

    private static VariableDeclarationNode Declaration(string name, ValueNode initializer) =>
        new(DataType.UINT16, name, initializer);

    private static VariableReferenceNode Reference(string name) =>
        new(DataType.UINT16, name);

    private static BinaryOperationNode Assign(ValueNode left, ValueNode right) =>
        new(DataType.UINT16, left, BinaryOperation.ASSIGN, right);

    private static IrBlock Block(ICfgNode origin, params IVisitableAstNode[] nodes) {
        IrBlock block = new(0);
        block.Statements.Add(IrStatement.Create(new InstructionStartNode("nop"), origin));
        foreach (IVisitableAstNode node in nodes) {
            block.Statements.Add(IrStatement.Create(node, origin));
        }
        block.Terminator = new IrExit(origin, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        return block;
    }

    private static IrMethod Method(ICfgNode origin, IrInstructionShape shape, params IrBlock[] blocks) =>
        new("M", [.. blocks], [new IrEntry(origin, blocks[0])], new HashSet<ICfgNode> { origin },
            new Dictionary<ICfgNode, IrInstructionShape> { [origin] = shape });
}
