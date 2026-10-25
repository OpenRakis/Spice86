namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using System.Collections.Immutable;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class CSharpStatementPrinterTest {
    private const string TargetLabel = "L_1000_0010";
    private static readonly RegisterNode Ax = new(DataType.UINT16, (int)RegisterIndex.AxIndex);
    private readonly ICfgNode _target = Substitute.For<ICfgNode>();

    [Fact]
    public void InstructionBoundaryScopesTheLocalVariableSuffix() {
        // Arrange
        VariableDeclarationNode declaration = new(DataType.UINT16, "result", Ax);
        BlockNode block = new(
            new InstructionBoundaryNode(_target, "0010", new BlockNode(declaration, new MethodCallNode(null, "Use", declaration.Reference))),
            new MethodCallNode(null, "Use", declaration.Reference));

        // Act
        string printed = Print(block);

        // Assert
        printed.Should().Be(Lines(
            "ushort result_0010 = AX;",
            "Use(result_0010);",
            "Use(result);"));
    }

    [Fact]
    public void ExpressionPrinterRejectsStatementNodes() {
        // Arrange
        CSharpExpressionPrinter expressions = CreateExpressionPrinter();

        // Act
        IVisitableAstNode[] statements = [
            new GotoNode(_target), new ContinueNode(), new InfiniteLoopNode(new BlockNode()),
            new DoWhileNode(new BlockNode(), new ConstantNode(DataType.BOOL, 1))
        ];

        // Assert
        foreach (IVisitableAstNode statement in statements) {
            Action print = () => expressions.Print(statement);
            print.Should().Throw<NotSupportedException>(statement.GetType().Name);
        }
    }

    [Fact]
    public void UnknownSegmentVariableIsAGeneratorBug() {
        // Arrange
        CSharpExpressionPrinter expressions = CreateExpressionPrinter();

        // Act
        Action print = () => expressions.Print(new SegmentVariableNode(0x2000));

        // Assert
        print.Should().Throw<InvalidOperationException>().WithMessage("*0x2000*");
    }

    [Fact]
    public void RuntimeVisitorsCannotVisitCodeGenerationNodes() {
        // Arrange
        IAstVisitor<int> runtimeVisitor = Substitute.For<IAstVisitor<int>>();

        // Act
        IVisitableAstNode[] nodes = [new BreakNode(), new ContinueNode(), new InfiniteLoopNode(new BlockNode())];

        // Assert
        foreach (IVisitableAstNode node in nodes) {
            Action visit = () => node.Accept(runtimeVisitor);
            visit.Should().Throw<NotSupportedException>(node.GetType().Name);
        }
    }

    [Fact]
    public void ImmediateOneIsAnIncrementWhileItIsUsedAsAValue() {
        // Arrange
        InstructionField<ushort> field = new(1, 2, 0x00100, 1, ImmutableList.Create<byte?>(1, 0), true);
        BinaryOperationNode assignment = Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS,
            new InstructionFieldNode(DataType.UINT16, field, 1)));

        // Act
        string printedAsValue = Print(assignment);
        field.UseValue = false;
        string printedAsMemoryRead = Print(assignment);

        // Assert
        printedAsValue.Should().Be(Lines("AX++;"));
        printedAsMemoryRead.Should().NotContain("++");
    }

    [Fact]
    public void OtherStepKeepsTheAssignment() {
        // Arrange
        BinaryOperationNode assignment = Assign(Ax, new BinaryOperationNode(DataType.UINT16, Ax, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 2)));

        // Act
        string printed = Print(assignment);

        // Assert
        printed.Should().Be(Lines("AX = (ushort)(AX + 2);"));
    }

    [Fact]
    public void OtherRegisterKeepsTheAssignment() {
        // Arrange
        RegisterNode bx = new(DataType.UINT16, (int)RegisterIndex.BxIndex);
        BinaryOperationNode assignment = Assign(Ax, new BinaryOperationNode(DataType.UINT16, bx, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1)));

        // Act
        string printed = Print(assignment);

        // Assert
        printed.Should().Be(Lines("AX = (ushort)(BX + 1);"));
    }

    [Fact]
    public void WiderArithmeticKeepsTheAssignment() {
        // Arrange
        BinaryOperationNode assignment = Assign(Ax, new BinaryOperationNode(DataType.UINT32, Ax, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT32, 1)));

        // Act
        string printed = Print(assignment);

        // Assert
        printed.Should().NotContain("++");
    }

    [Fact]
    public void MemoryDestinationKeepsTheAssignment() {
        // Arrange
        SegmentedPointerNode memory = new(DataType.UINT16, new SegmentRegisterNode((int)SegmentRegisterIndex.DsIndex), null,
            new ConstantNode(DataType.UINT16, 0x0010));
        BinaryOperationNode assignment = Assign(memory, new BinaryOperationNode(DataType.UINT16, memory, BinaryOperation.PLUS,
            new ConstantNode(DataType.UINT16, 1)));

        // Act
        string printed = Print(assignment);

        // Assert
        printed.Should().Be(Lines("UInt16[DS, 0x0010] = (ushort)(UInt16[DS, 0x0010] + 1);"));
    }

    [Fact]
    public void LabelEndingALoopBodyGetsAnEmptyStatement() {
        // Arrange
        InfiniteLoopNode loop = new(new BlockNode(new LabelNode(_target), new CommentNode("1000:0010 jmp short 0x0000")));

        // Act
        string printed = Print(loop);

        // Assert
        printed.Should().Be(Lines(
            "while (true) {",
            $"{TargetLabel}:",
            "    // 1000:0010 jmp short 0x0000",
            "    ;",
            "}"));
    }

    private static BinaryOperationNode Assign(ValueNode destination, ValueNode value) =>
        new(DataType.UINT16, destination, BinaryOperation.ASSIGN, value);

    private string Print(IVisitableAstNode node) {
        CSharpSourceWriter writer = new();
        CSharpStatementPrinter printer = new(writer, CreateExpressionPrinter(), LabelOf);
        printer.Print(node);
        return writer.ToString();
    }

    private static CSharpExpressionPrinter CreateExpressionPrinter() =>
        new(new Dictionary<ushort, string> { [0x1000] = "cs1", [0xF000] = "cs2" });

    private string LabelOf(ICfgNode node) {
        if (!ReferenceEquals(node, _target)) {
            throw new InvalidOperationException("Unexpected goto target.");
        }
        return TargetLabel;
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;
}
