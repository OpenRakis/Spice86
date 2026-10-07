namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
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

    [Fact]
    public void FlagEqualToTrueRendersBareFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new CpuFlagNode(Flags.Zero), BinaryOperation.EQUAL, new ConstantNode(DataType.BOOL, 1));

        CSharpFragment fragment = comparison.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("ZeroFlag");
    }

    [Fact]
    public void FlagEqualToFalseRendersNegatedFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new CpuFlagNode(Flags.Zero), BinaryOperation.EQUAL, new ConstantNode(DataType.BOOL, 0));

        CSharpFragment fragment = comparison.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("!ZeroFlag");
    }

    [Fact]
    public void FlagNotEqualToTrueRendersNegatedFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new CpuFlagNode(Flags.Zero), BinaryOperation.NOT_EQUAL, new ConstantNode(DataType.BOOL, 1));

        CSharpFragment fragment = comparison.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("!ZeroFlag");
    }

    [Fact]
    public void TrueEqualToFlagRendersBareFlag() {
        BinaryOperationNode comparison = new(DataType.BOOL, new ConstantNode(DataType.BOOL, 1), BinaryOperation.EQUAL, new CpuFlagNode(Flags.Zero));

        CSharpFragment fragment = comparison.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("ZeroFlag");
    }

    [Fact]
    public void WordEqualToZeroKeepsComparison() {
        BinaryOperationNode comparison = new(DataType.BOOL, new RegisterNode(DataType.UINT16, 1), BinaryOperation.EQUAL, new ConstantNode(DataType.UINT16, 0));

        CSharpFragment fragment = comparison.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("CX == 0");
    }

    [Fact]
    public void ZeroDisplacementRendersBareBaseRegisterOffset() {
        BinaryOperationNode offset = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 5), BinaryOperation.PLUS, new ConstantNode(DataType.INT8, 0));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, BP]");
    }

    [Fact]
    public void ZeroDisplacementKeepsCompoundOffsetWrapped() {
        BinaryOperationNode baseAndIndex = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 3), BinaryOperation.PLUS, new RegisterNode(DataType.UINT16, 6));
        BinaryOperationNode offset = new(DataType.UINT16, baseAndIndex, BinaryOperation.PLUS, new ConstantNode(DataType.INT8, 0));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, (ushort)(BX + SI)]");
    }

    [Fact]
    public void NegativeByteDisplacementRendersSubtraction() {
        BinaryOperationNode offset = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 5), BinaryOperation.PLUS, new ConstantNode(DataType.INT8, 0xF2));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, (ushort)(BP - 0x0E)]");
    }

    [Fact]
    public void NegativeWordDisplacementRendersSubtraction() {
        BinaryOperationNode baseAndIndex = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 5), BinaryOperation.PLUS, new RegisterNode(DataType.UINT16, 7));
        BinaryOperationNode offset = new(DataType.UINT16, baseAndIndex, BinaryOperation.PLUS, new ConstantNode(DataType.INT16, 0xFE3C));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, (ushort)(BP + DI - 0x01C4)]");
    }

    [Fact]
    public void ByteMinValueDisplacementRendersUnsignedMagnitude() {
        BinaryOperationNode offset = new(DataType.UINT16, new RegisterNode(DataType.UINT16, 5), BinaryOperation.PLUS, new ConstantNode(DataType.INT8, 0x80));

        CSharpFragment fragment = StackWord(offset);

        fragment.Text.Should().Be("UInt16[SS, (ushort)(BP - 0x80)]");
    }

    [Fact]
    public void Int32MinValueDisplacementKeepsAddition() {
        BinaryOperationNode sum = new(DataType.UINT32, new RegisterNode(DataType.UINT32, 3), BinaryOperation.PLUS, new ConstantNode(DataType.INT32, 0x80000000));

        CSharpFragment fragment = sum.Accept(_emitter).AsExpression();

        fragment.Text.Should().Be("EBX + int.MinValue");
    }

    private CSharpFragment StackWord(ValueNode offset) {
        SegmentedPointerNode pointer = new(DataType.UINT16, new SegmentRegisterNode(2), null, offset);
        return pointer.Accept(_emitter).AsExpression();
    }
}
