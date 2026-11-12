namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Ssa;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.CfgCpu.Blocks;

using Xunit;

/// <summary>
/// Preservation through cycles: a version computed under a loop-head hypothesis must not keep the
/// hypothesis once the head converges lower.
/// </summary>
public sealed class IrPreservationTest {
    /// <summary>Loop head phi P1 = phi(entry, X); the back-edge X = phi(X_a, w) merges the call
    /// block's version with a sibling that writes EBX. Evaluating P1 visits X_a while P1 is still
    /// under hypothesis; X_a must not keep the hypothesis: its real answer is P1's, which is none.
    /// </summary>
    [Fact]
    public void CallDefinedVersionInACycleKeepsNoHypothesisBytes() {
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock head = Block(1);
        IrBlock call = Block(2);
        IrBlock write = Block(3);
        IrBlock next = Block(4);
        IrBlock exit = Block(5);
        IrStatement callStatement = CallDefining(instruction);
        IrEditor.WithoutSsa.SetTerminator(entry,
            new IrJump(instruction, new IrEdge(head, false)));
        IrEditor.WithoutSsa.SetTerminator(head,
            new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
                new IrEdge(call, false), new IrEdge(write, false)));
        IrEditor.WithoutSsa.Append(call, callStatement);
        IrEditor.WithoutSsa.SetTerminator(call,
            new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
                new IrEdge(next, false), new IrEdge(exit, false)));
        IrEditor.WithoutSsa.Append(write, Writes(instruction, CpuValues.Ebx));
        IrEditor.WithoutSsa.SetTerminator(write,
            new IrJump(instruction, new IrEdge(next, false)));
        IrEditor.WithoutSsa.SetTerminator(next,
            new IrJump(instruction, new IrEdge(head, false)));
        IrEditor.WithoutSsa.SetTerminator(exit, new IrExit(instruction, ReturnValue()));
        IrMethod method = Method([Entry(instruction, entry)], [instruction],
            entry, head, call, write, next, exit);
        IrSsa ssa = IrSsa.Build(method);
        CpuValues callChanges = CpuValues.Al | CpuValues.Cx | CpuValues.Dx;
        IrPreservation preservation = new(method, ssa, IrStackSlots.Empty,
            statement => statement == callStatement ? callChanges : null);
        int headPhi = ssa.PhisOf(head).Single(phi => phi.Value == IrSsaValue.Ebx).Result;
        int callVersion = ssa.Definitions(callStatement)
            .Single(definition => definition.Value == IrSsaValue.Ebx).Version;

        preservation.Preserved(IrSsaValue.Ebx, headPhi)
            .Should().Be(CpuValues.None, "the write arm clobbers EBX");
        preservation.Preserved(IrSsaValue.Ebx, callVersion)
            .Should().Be(CpuValues.None,
                "the version after the call feeds the same phi: it is exactly as preserved as the loop head");
    }

    /// <summary>A call-shaped statement: it defines versions for every value the call site covers,
    /// while its transfer effect only reports the callee's real changes.</summary>
    private static IrStatement CallDefining(ICfgNode origin) =>
        new(new CommentNode("call"), origin,
            new IrEffects(CpuValues.None, CpuValues.None,
                CpuValues.Al | CpuValues.Cx | CpuValues.Dx | CpuValues.Ebx,
                IrTokens.None, IrTokens.None, false, false));

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

    private static ReturnNode ReturnValue() => new(new ConstantNode(DataType.UINT16, 0));
}
