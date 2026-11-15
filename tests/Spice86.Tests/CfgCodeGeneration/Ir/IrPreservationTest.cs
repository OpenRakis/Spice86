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

    /// <summary>A loop of <see cref="Joins"/> sequential if/else joins, each left arm partially
    /// writing EAX (so EAX merges at every join and the preservation recursion doubles per join),
    /// each right arm writing a register cycling through <see cref="ArmRegisters"/>. EBX is never
    /// written, so every EBX version keeps all its entry bytes. The recursive algorithm revisits the
    /// whole join chain for each EAX operand inside the loop-head frame: about 2^<see cref="Joins"/>
    /// evaluations; the fixed point answers after a few linear passes.</summary>
    [Fact]
    public void ManyJoinsInALoopPreserveAnUnwrittenRegisterFast() {
        (IrMethod method, IrBlock head, IrBlock exit) = LoopWithJoins(-1);
        IrSsa ssa = IrSsa.Build(method);
        IrPreservation preservation = new(method, ssa, IrStackSlots.Empty, _ => null);
        int exitEbx = ssa.VersionBefore(exit, 0, IrSsaValue.Ebx);

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (IrSsaValue value in Enum.GetValues<IrSsaValue>()) {
            foreach ((IrBlock _, int version) in AllVersions(ssa, value)) {
                preservation.Preserved(value, version);
            }
        }
        watch.Stop();

        preservation.Preserved(IrSsaValue.Ebx, exitEbx)
            .Should().Be(IrSsaValues.MaskOf(IrSsaValue.Ebx), "EBX is never written in the loop");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "preservation is a fixed point per value, not an exponential recursion");
    }

    /// <summary>The same shape with one arm writing EBX: the write clobbers it, so no EBX byte is
    /// preserved at the loop head nor at the exit.</summary>
    [Fact]
    public void ManyJoinsInALoopDoNotPreserveAWrittenRegister() {
        (IrMethod method, IrBlock head, IrBlock exit) = LoopWithJoins(3);
        IrSsa ssa = IrSsa.Build(method);
        IrPreservation preservation = new(method, ssa, IrStackSlots.Empty, _ => null);

        int headPhi = ssa.PhisOf(head).Single(phi => phi.Value == IrSsaValue.Ebx).Result;
        preservation.Preserved(IrSsaValue.Ebx, headPhi).Should().Be(CpuValues.None,
            "one arm of the loop writes EBX");
        preservation.Preserved(IrSsaValue.Ebx, ssa.VersionBefore(exit, 0, IrSsaValue.Ebx))
            .Should().Be(CpuValues.None, "the exit sees the clobbered loop-head version");
    }

    private const int Joins = 30;

    private static readonly IrSsaValue[] ArmRegisters =
        [IrSsaValue.Ecx, IrSsaValue.Edx, IrSsaValue.Ebp, IrSsaValue.Esi, IrSsaValue.Edi];

    /// <summary>The versions SSA assigned to <paramref name="value"/>, as (block, version).</summary>
    private static IEnumerable<(IrBlock, int)> AllVersions(IrSsa ssa, IrSsaValue value) {
        for (int version = 0; version < ssa.VersionCount; version++) {
            IrSsaVersionInfo info = ssa.Info(version);
            if (info.Value == value) {
                yield return (info.Block, version);
            }
        }
    }

    /// <summary>Builds entry -> head -> <see cref="Joins"/> sequential if/else joins whose last join
    /// jumps back to head; head also has an exit edge. Each left arm partially writes EAX, each
    /// right arm fully writes a register cycling through <see cref="ArmRegisters"/>. Arm
    /// <paramref name="ebxArm"/> (a flat arm index) writes EBX instead when non-negative.</summary>
    private static (IrMethod, IrBlock Head, IrBlock Exit) LoopWithJoins(int ebxArm) {
        CfgInstruction instruction = Instruction(0);
        IrBlock entry = Block(0);
        IrBlock head = Block(1);
        IrBlock join = Block(2);
        IrBlock exit = Block(3);
        List<IrBlock> blocks = [entry, head, join, exit];
        IrEditor.WithoutSsa.SetTerminator(entry,
            new IrJump(instruction, new IrEdge(head, false)));
        IrEditor.WithoutSsa.SetTerminator(head,
            new IrBranch(instruction, new CpuFlagNode(Flags.Zero),
                new IrEdge(join, false), new IrEdge(exit, false)));
        IrEditor.WithoutSsa.SetTerminator(exit, new IrExit(instruction, ReturnValue()));
        int nextId = 4;
        int arm = 0;
        for (int i = 0; i < Joins; i++) {
            IrBlock target = i == Joins - 1 ? head : Block(nextId++);
            if (i != Joins - 1) {
                blocks.Add(target);
            }
            IrBlock left = Block(nextId++);
            IrBlock right = Block(nextId++);
            blocks.Add(left);
            blocks.Add(right);
            IrSsaValue leftValue = arm == ebxArm ? IrSsaValue.Ebx : IrSsaValue.Eax;
            arm++;
            IrSsaValue rightValue = arm == ebxArm ? IrSsaValue.Ebx : ArmRegisters[arm % ArmRegisters.Length];
            arm++;
            IrEditor.WithoutSsa.Append(left, PartialWrites(instruction, IrSsaValues.MaskOf(leftValue)));
            IrEditor.WithoutSsa.Append(right, Writes(instruction, IrSsaValues.MaskOf(rightValue)));
            IrEditor.WithoutSsa.SetTerminator(left,
                new IrJump(instruction, new IrEdge(target, false)));
            IrEditor.WithoutSsa.SetTerminator(right,
                new IrJump(instruction, new IrEdge(target, false)));
            IrEditor.WithoutSsa.SetTerminator(join,
                new IrBranch(instruction, new CpuFlagNode(Flags.Carry),
                    new IrEdge(left, false), new IrEdge(right, false)));
            join = target;
        }
        IrMethod method = Method([Entry(instruction, entry)], [instruction], [.. blocks]);
        return (method, head, exit);
    }

    /// <summary>A statement that may write the register bytes of <paramref name="mask"/> without a
    /// guaranteed full write: a partial definition whose new version still depends on the previous
    /// one, which is what chains the preservation recursion through the join phis.</summary>
    private static IrStatement PartialWrites(ICfgNode origin, CpuValues mask) =>
        new(new CommentNode("p"), origin,
            new IrEffects(CpuValues.None, CpuValues.None, mask, IrTokens.None, IrTokens.None, false, false));

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
