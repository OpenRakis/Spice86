namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The SSA webs: a phi joins the versions of its operands and its result into one web, a partial
/// definition keeps its previous version's web unless the statement fully writes the register's low
/// word, and every version belongs to one of <see cref="IrSsaWebs.WebCount"/> webs.
/// </summary>
public sealed class IrSsaWebsTest {
    private static readonly IrValues Eax = IrSsaValues.MaskOf(IrSsaValue.Eax);
    private static readonly IrValues Ax = (IrValues)0x3UL;

    [Fact]
    public void TwoWritesJoinedByAPhiShareOneWeb() {
        // Arrange: entry branches on Carry, both arms write EAX, the join reads it
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        entry.Terminator = new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, false), new IrEdge(falseArm, false));
        IrStatement defineTrue = Writes(instruction, Eax);
        IrStatement defineFalse = Writes(instruction, Eax);
        trueArm.Statements.Add(defineTrue);
        falseArm.Statements.Add(defineFalse);
        trueArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        falseArm.Terminator = new IrJump(instruction, new IrEdge(join, false));
        join.Statements.Add(Reads(instruction, Eax));
        join.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);

        // Act
        IrSsa ssa = IrSsa.Build(method);
        IrSsaWebs webs = IrSsaWebs.Build(ssa);

        // Assert
        int webTrue = ssa.Definitions(defineTrue).Single().Version;
        int webFalse = ssa.Definitions(defineFalse).Single().Version;
        IrSsaPhi phi = ssa.Phis.Should().ContainSingle(p => p.Value == IrSsaValue.Eax).Subject;
        webs.WebOf(phi.Result).Should().Be(webs.WebOf(webTrue));
        webs.WebOf(phi.Result).Should().Be(webs.WebOf(webFalse));
    }

    [Fact]
    public void AFullLowWordWriteAfterAPartialOneStartsANewWeb() {
        // Arrange: a write of AL then a full write of AX in one straight-line block
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement partial = new(new CommentNode("partial"), instruction,
            new IrEffects(Eax, (IrValues)0x1UL, Eax, IrTokens.None, IrTokens.None, false, false));
        IrStatement lowWord = new(new CommentNode("ax"), instruction,
            new IrEffects(Eax, Ax, Eax & ~Ax, IrTokens.None, IrTokens.None, false, false));
        block.Statements.Add(partial);
        block.Statements.Add(lowWord);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);
        IrSsaWebs webs = IrSsaWebs.Build(ssa);

        // Assert
        int first = ssa.Definitions(partial).Single().Version;
        int second = ssa.Definitions(lowWord).Single().Version;
        ssa.Info(first).Kind.Should().Be(IrSsaDefinitionKind.PartialDefinition);
        ssa.Info(second).Kind.Should().Be(IrSsaDefinitionKind.PartialDefinition);
        webs.WebOf(first).Should().NotBe(webs.WebOf(second));
    }

    [Fact]
    public void APartialWriteWithoutTheFullLowWordKeepsTheWeb() {
        // Arrange: a write of AL then another write of AL only
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement partial = new(new CommentNode("partial"), instruction,
            new IrEffects(Eax, (IrValues)0x1UL, Eax, IrTokens.None, IrTokens.None, false, false));
        IrStatement lowByte = new(new CommentNode("al"), instruction,
            new IrEffects(Eax, (IrValues)0x1UL, Eax, IrTokens.None, IrTokens.None,
                false, false));
        block.Statements.Add(partial);
        block.Statements.Add(lowByte);
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);
        IrSsaWebs webs = IrSsaWebs.Build(ssa);

        // Assert
        int first = ssa.Definitions(partial).Single().Version;
        int second = ssa.Definitions(lowByte).Single().Version;
        webs.WebOf(first).Should().Be(webs.WebOf(second));
    }

    [Fact]
    public void WebCountIsTheNumberOfDistinctWebs() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        block.Statements.Add(Writes(instruction, Eax));
        block.Statements.Add(Writes(instruction, IrSsaValues.MaskOf(IrSsaValue.Ecx)));
        block.Terminator = new IrExit(instruction, ReturnValue());
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);

        // Act
        IrSsa ssa = IrSsa.Build(method);
        IrSsaWebs webs = IrSsaWebs.Build(ssa);

        // Assert
        HashSet<int> distinct = [];
        for (int version = 0; version < ssa.VersionCount; version++) {
            distinct.Add(webs.WebOf(version));
        }
        webs.WebCount.Should().Be(distinct.Count);
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
}
