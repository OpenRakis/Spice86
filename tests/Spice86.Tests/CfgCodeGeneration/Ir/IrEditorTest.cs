namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// The <see cref="IrEditor"/> contract: every statement or terminator edit goes through it, the SSA
/// records it keeps are derived from the replacement's effects, and an edit the SSA cannot keep
/// exact marks it stale so every query throws until the pipeline rebuilds it.
/// </summary>
public sealed class IrEditorTest {
    private static readonly CpuValues Eax = CpuValues.Eax;
    private static readonly CpuValues Ebx = CpuValues.Ebx;

    [Fact]
    public void QueryOnAStaleSsaThrows() {
        // Arrange: the removed statement defines the only version of Eax the reader uses.
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrStatement mov = Writes(instruction, Eax);
        IrEditor.WithoutSsa.Append(block, mov);
        IrEditor.WithoutSsa.Append(block, Reads(instruction, Eax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Remove(block, 0);

        // Assert
        ssa.IsStale.Should().BeTrue();
        Action act = () => ssa.VersionBefore(block, 1, IrSsaValue.Eax);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LivenessQueryAfterAJoinUseChangeThrowsUntilRecomputed() {
        // Arrange: a diamond whose join holds the only live use of the Eax phi.
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock trueArm = Block(1);
        IrBlock falseArm = Block(2);
        IrBlock join = Block(3);
        IrStatement trueWrite = Writes(instruction, Eax);
        IrStatement falseWrite = Writes(instruction, Eax);
        IrStatement read = Reads(instruction, Eax);
        IrEditor.WithoutSsa.Append(trueArm, trueWrite);
        IrEditor.WithoutSsa.Append(falseArm, falseWrite);
        IrEditor.WithoutSsa.Append(join, read);
        IrEditor.WithoutSsa.SetTerminator(entry, new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
            new IrEdge(trueArm, true), new IrEdge(falseArm, true)));
        IrEditor.WithoutSsa.SetTerminator(trueArm, new IrJump(instruction, new IrEdge(join, false)));
        IrEditor.WithoutSsa.SetTerminator(falseArm, new IrJump(instruction, new IrEdge(join, false)));
        IrEditor.WithoutSsa.SetTerminator(join, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, trueArm, falseArm, join);
        IrSsa ssa = IrSsa.Build(method);
        int phiVersion = ssa.PhisOf(join).Single(phi => phi.Value == IrSsaValue.Eax).Result;
        int liveBefore = ssa.LiveUses(phiVersion).Count;
        liveBefore.Should().BeGreaterThan(0);

        // Act: deleting a live use invalidates the liveness, not the versions.
        new IrEditor(ssa, new GenerationStepTimer()).Remove(join, 0);

        // Assert
        ssa.IsStale.Should().BeFalse();
        Action stale = () => ssa.LiveUses(phiVersion);
        stale.Should().Throw<InvalidOperationException>();
        ssa.RecomputeLiveness();
        ssa.LiveUses(phiVersion).Count.Should().Be(liveBefore - 1);
        IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
    }

    [Fact]
    public void ReplacementReadsAreDerivedFromEffects() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrEditor.WithoutSsa.Append(block, Writes(instruction, Eax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);
        IrStatement reader = new(new CommentNode("r"), instruction,
            new IrEffects(Ebx, Eax, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

        // Act
        new IrEditor(ssa, new GenerationStepTimer()).Replace(block, 0, reader);

        // Assert: the Ebx read the original did not make is recorded, and the maintained SSA
        // matches a fresh build.
        IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
        ssa.UsesBySite[reader].Should().Contain(use => use.Value == IrSsaValue.Ebx);
    }

    [Fact]
    public void FreshBuildCheckFailsAfterAnEditTheSsaDidNotSee() {
        // Arrange
        CfgInstruction instruction = Instruction(0);
        IrBlock block = Block(0);
        IrEditor.WithoutSsa.Append(block, Writes(instruction, Eax));
        IrEditor.WithoutSsa.SetTerminator(block, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, block)], [instruction], block);
        IrSsa ssa = IrSsa.Build(method);

        // Act: the construction editor changes the IR without the SSA knowing.
        IrEditor.WithoutSsa.Replace(block, 0, Writes(instruction, Ebx));

        // Assert
        Action act = () => IrVerifier.VerifySsaMatchesFreshBuild(method, ssa);
        act.Should().Throw<InvalidOperationException>().WithMessage("*fresh build*");
    }

    private static IrMethod Method(IrEntry[] entries, ICfgNode[] nodes, params IrBlock[] blocks) =>
        new("M", [.. blocks], entries, new HashSet<ICfgNode>(nodes),
            new Dictionary<ICfgNode, IrInstructionShape>());

    private static IrEntry Entry(ICfgNode node, IrBlock block) => new(node, block);

    private static IrBlock Block(int id) => new(id);

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));

    private static IrStatement Writes(ICfgNode origin, CpuValues mask) =>
        new(new CommentNode("w"), origin,
            new IrEffects(CpuValues.None, mask, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

    private static IrStatement Reads(ICfgNode origin, CpuValues values) =>
        new(new CommentNode("r"), origin,
            new IrEffects(values, CpuValues.None, CpuValues.None, IrTokens.None, IrTokens.None, false, false));

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
