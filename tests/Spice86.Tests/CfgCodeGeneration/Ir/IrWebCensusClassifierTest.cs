namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The web census classification on hand-built IR methods: a definition read by an exit is
/// store-only, an entry version read by an instruction but not by an exit is load-only, a
/// definition whose uses are all inside the method is pure, and an entry version flowing to an
/// exit is a through web.
/// </summary>
public sealed class IrWebCensusClassifierTest {
    private static readonly IrValues Eax = IrSsaValues.MaskOf(IrSsaValue.Eax);

    [Fact]
    public void ADefinitionReadByAnExitIsStoreOnly() {
        // Arrange: mov-like write of EAX, one instruction read, then an exit reading everything
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(Writes(instruction, Eax));
        block.Statements.Add(Reads(instruction, Eax));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        WebCensus census = Classify(method);

        // Assert: the EAX definition web leaves to the register with one instruction use; the
        // entry versions of the other counted registers flow to the exit as through webs.
        census.Webs.Should().Be(9);
        census.StoreOnlyWebs.Should().Be(1);
        census.StoreOnlyUses.Should().Be(1);
        census.ThroughWebs.Should().Be(8);
        census.ThroughUses.Should().Be(0);
        census.PureWebs.Should().Be(0);
        census.LoadOnlyWebs.Should().Be(0);
    }

    [Fact]
    public void AnEntryVersionReadInsideIsLoadOnly() {
        // Arrange: an instruction reads EAX without any write; the abort exit reads nothing
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(Reads(instruction, Eax));
        block.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        WebCensus census = Classify(method);

        // Assert
        census.Webs.Should().Be(1);
        census.LoadOnlyWebs.Should().Be(1);
        census.LoadOnlyUses.Should().Be(1);
    }

    [Fact]
    public void ADefinitionUsedOnlyInsideIsPureAndSpansBlocks() {
        // Arrange: EAX defined in the entry block, read in a successor, abort exit
        CfgInstruction instruction = Instruction(0);
        IrBlock define = Block(0);
        IrBlock read = Block(1);
        define.Statements.Add(Writes(instruction, Eax));
        define.Terminator = new IrJump(instruction, new IrEdge(read, false));
        read.Statements.Add(Reads(instruction, Eax));
        read.Terminator = new IrExit(instruction, Fail());
        IrMethod method = Method([Entry(instruction, define)], [instruction], define, read);

        // Act
        WebCensus census = Classify(method);

        // Assert
        census.Webs.Should().Be(1);
        census.PureWebs.Should().Be(1);
        census.PureUses.Should().Be(1);
        census.PureMultiBlockWebs.Should().Be(1);
    }

    private static WebCensus Classify(IrMethod method) {
        IrSsa ssa = IrSsa.Build(method);
        IrSsaWebs webs = IrSsaWebs.Build(ssa);
        return IrWebCensusClassifier.Classify(ssa, webs, new HashSet<ICfgNode>());
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static IrStatement Writes(ICfgNode origin, IrValues mask) =>
        new(new CommentNode("w"), origin,
            new IrEffects(IrValues.None, mask, IrValues.None, IrTokens.None, IrTokens.None,
                false, false));

    private static IrStatement Reads(ICfgNode origin, IrValues values) =>
        new(new CommentNode("r"), origin,
            new IrEffects(values, IrValues.None, IrValues.None, IrTokens.None, IrTokens.None,
                false, false));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));

    private static FailAsUntestedNode Fail() => new([UntestedMessagePart.Literal("untested")]);
}
