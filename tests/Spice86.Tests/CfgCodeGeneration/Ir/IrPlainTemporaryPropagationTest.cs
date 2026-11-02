namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Passes;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// A plain temporary is inlined into its readers; each legality refusal keeps the declaration.
/// </summary>
public sealed class IrPlainTemporaryPropagationTest {
    private static readonly RegisterNode Ax = new(DataType.UINT16, 0);
    private static readonly RegisterNode Cx = new(DataType.UINT16, 1);
    private static readonly RegisterNode Bx = new(DataType.UINT16, 3);

    [Fact]
    public void PlainInitializer_PropagatesToReader() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeEquivalentTo(Assign(Bx, Ax));
    }

    [Fact]
    public void PlainInitializer_PropagatesIntoExitTransfer() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax));
        block.Terminator = new IrExit(instruction, new ReturnNode(Reference("tmp")));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().ContainSingle()
            .Which.Node.Should().BeOfType<InstructionStartNode>();
        block.Terminator.Should().BeEquivalentTo(new IrExit(instruction, new ReturnNode(Ax)));
    }

    [Fact]
    public void MemoryReadingInitializer_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IVisitableAstNode declaration = Declaration("tmp",
            new AbsolutePointerNode(DataType.UINT16, new ConstantNode(DataType.UINT32, 0x10)));
        IrBlock block = Block(instruction, declaration, Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().Be(declaration);
    }

    [Fact]
    public void VariableAssignedAgain_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Reference("tmp"), Cx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void NestedReference_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IVisitableAstNode loop = new WhileNode(new ConstantNode(DataType.BOOL, 1),
            new BlockNode([Assign(Bx, Reference("tmp"))]));
        IrBlock block = Block(instruction, Declaration("tmp", Ax));
        block.Statements.Add(new IrStatement(loop, instruction, IrEffects.None));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void WriterBetweenDeclarationAndRead_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Ax, Cx), Assign(Bx, Reference("tmp")));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(4);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void ReadFromAnotherInstruction_IsRefused() {
        // Arrange
        CfgInstruction first = Instruction(0);
        CfgInstruction second = Instruction(1);
        IrBlock block = Block(first, Declaration("tmp", Ax));
        block.Statements.Add(IrStatement.Create(Assign(Bx, Reference("tmp")), second));
        IrMethod method = Method(first, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void ReadFromAnotherBlock_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(instruction, Declaration("tmp", Ax),
            Assign(Bx, Reference("tmp")));
        IrBlock other = new(1);
        other.Statements.Add(IrStatement.Create(Assign(Cx, Reference("tmp")), instruction));
        other.Terminator = new IrExit(instruction, new ReturnNode(new ConstantNode(DataType.UINT16, 0)));
        IrMethod method = Method(instruction, block, other);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeOfType<VariableDeclarationNode>();
    }

    [Fact]
    public void PointerOffsetRead_Propagates() {
        // Arrange: the les/lds shape — one offset temporary read inside two pointer addresses.
        CfgInstruction instruction = Instruction(0);
        SegmentRegisterNode es = new(1);
        VariableReferenceNode x = Reference("lxsOffset");
        VariableDeclarationNode declaration = Declaration("lxsOffset",
            new ConstantNode(DataType.UINT16, 0));
        IVisitableAstNode valueRead = Assign(Bx,
            new SegmentedPointerNode(DataType.UINT16, es, null, x));
        IVisitableAstNode segmentRead = Assign(new SegmentRegisterNode(1),
            new SegmentedPointerNode(DataType.UINT16, es, null,
                new TypeConversionNode(DataType.UINT16,
                    new BinaryOperationNode(DataType.UINT32, x, BinaryOperation.PLUS,
                        new ConstantNode(DataType.UINT32, 2)))));
        IrBlock block = Block(instruction, declaration, valueRead, segmentRead);
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void DivisionOfAnotherInstruction_IsNotAReader() {
        // Arrange: a division statement of a later instruction shares the block; it does not read
        // the temporary, so the propagation into the readers must still happen.
        CfgInstruction instruction = Instruction(0);
        CfgInstruction divisionInstruction = Instruction(1);
        IVisitableAstNode declaration = Declaration("offset", Ax);
        IVisitableAstNode read = Assign(Bx, Reference("offset"));
        IrBlock block = Block(instruction, declaration, read);
        block.Statements.Add(IrStatement.Create(
            new DivisionNode(new DivisionPattern(BitWidth.WORD_16, false,
                new ConstantNode(DataType.UINT32, 7), Cx,
                new RegisterNode(DataType.UINT16, 0), new RegisterNode(DataType.UINT16, 2))),
            divisionInstruction));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().BeEquivalentTo(Assign(Bx, Ax));
    }

    [Fact]
    public void DivisionReadingTheVariable_IsRefused() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        VariableDeclarationNode declaration = Declaration("dividend", Ax);
        IrBlock block = Block(instruction, declaration);
        block.Statements.Add(IrStatement.Create(
            new DivisionNode(new DivisionPattern(BitWidth.WORD_16, false,
                Reference("dividend"), Cx,
                new RegisterNode(DataType.UINT16, 0), new RegisterNode(DataType.UINT16, 2))),
            instruction));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(3);
        block.Statements[1].Node.Should().Be(declaration);
    }

    [Fact]
    public void ReferenceInsideAConditional_Propagates() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        ConditionalValueNode conditional = new(DataType.UINT16, new CpuFlagNode(Flags.Carry),
            Reference("tmp"), Cx);
        IrBlock block = Block(instruction, Declaration("tmp", Ax), Assign(Bx, conditional));
        IrMethod method = Method(instruction, block);

        // Act
        IrPlainTemporaryPropagation.Apply(method);

        // Assert
        block.Statements.Should().HaveCount(2);
        block.Statements[1].Node.Should().BeEquivalentTo(Assign(Bx,
            new ConditionalValueNode(DataType.UINT16, new CpuFlagNode(Flags.Carry), Ax, Cx)));
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

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

    private static IrMethod Method(ICfgNode origin, params IrBlock[] blocks) =>
        new("M", [.. blocks], [new IrEntry(origin, blocks[0])], new HashSet<ICfgNode> { origin },
            new Dictionary<ICfgNode, IrInstructionShape>());
}
