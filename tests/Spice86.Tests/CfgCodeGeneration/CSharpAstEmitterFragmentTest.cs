namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;

using Xunit;

/// <summary>
/// Locks in the behavior-preserving contract for the expression arm of <see cref="EmittedCode"/>: the
/// fragment <see cref="CSharpAstEmitter"/> returns for an expression-shaped node must render exactly the
/// expected text for representative AST nodes (register, memory, binary op, constant). The fragment is a
/// transparent carrier of the rendered text. Expression lowering reads no CFG context, so the emitter is
/// built over an empty context.
/// </summary>
public class CSharpAstEmitterFragmentTest {
    private readonly CSharpAstEmitter _emitter = CreateExpressionEmitter();

    private static CSharpAstEmitter CreateExpressionEmitter() {
        CfgPartitionedProgram program = new() { Partitions = [], Transfers = [] };
        CfgGeneratorContext context = new(
            program,
            partitionByNode: new(),
            methodNames: new(),
            partitionBaseNames: new(),
            labels: new(),
            segmentVariables: new(),
            transfersByEdge: new(),
            entriesByPartition: new(),
            blockEntryByAddress: new());
        return new CSharpAstEmitter(context, new TransferEmitter(context));
    }

    [Fact]
    public void RegisterNodeRendersBareRegisterName() {
        RegisterNode register = new(DataType.UINT16, 0); // AX

        CSharpFragment fragment = register.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("AX");
    }

    [Fact]
    public void ConstantNodeRendersUnprefixedLiteral() {
        ConstantNode constant = new(DataType.UINT16, 0x111C);

        CSharpFragment fragment = constant.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("0x111C");
    }

    [Fact]
    public void SegmentedPointerNodeRendersMemoryIndexer() {
        SegmentRegisterNode segment = new(3); // DS
        ConstantNode offset = new(DataType.UINT16, 0x50);
        SegmentedPointerNode pointer = new(DataType.UINT16, segment, null, offset);

        CSharpFragment fragment = pointer.Accept(_emitter).AsExpression();

        // A constant offset is emitted directly (the indexer has a ushort overload), not wrapped in (uint).
        fragment.Text.Should().Be("UInt16[DS, 0x0050]");
    }

    [Fact]
    public void BinaryOperationNodeRendersParenthesizedExpression() {
        RegisterNode left = new(DataType.UINT16, 0); // AX
        ConstantNode right = new(DataType.UINT16, 0x1);
        BinaryOperationNode addition = new(DataType.UINT16, left, BinaryOperation.PLUS, right);

        CSharpFragment fragment = addition.Accept(_emitter).AsExpression();

        // Redundant outer parentheses are dropped; precedence-required ones are kept.
        fragment.Text.Should().Be("AX + 1");
    }

    [Fact]
    public void NegateOfNegativeConstantIsParenthesized() {
        ConstantNode inner = new(DataType.INT16, 0xFFFE); // -2
        UnaryOperationNode negate = new(DataType.INT16, UnaryOperation.NEGATE, inner);

        CSharpFragment fragment = negate.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("-(-2)");
    }

    [Fact]
    public void FragmentTextEqualsToString() {
        ConstantNode constant = new(DataType.UINT16, 0x2A);

        CSharpFragment fragment = constant.Accept(_emitter).AsExpression();

        fragment.ToString().Should().Be(fragment.Text);
    }
}
