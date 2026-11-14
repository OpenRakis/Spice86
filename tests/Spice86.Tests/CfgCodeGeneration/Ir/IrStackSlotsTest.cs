namespace Spice86.Tests.CfgCodeGeneration.Ir;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
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
/// The proved-call rule of <see cref="IrStackSlots"/>: a full memory boundary statement keeps the
/// stack offset's base, advanced by the callee's pop count, only when the provider proves the call
/// returns to its caller with a <c>ret n</c>; every other boundary still rebases.
/// </summary>
public sealed class IrStackSlotsTest {
    /// <summary>A call the provider does not prove - an unproved callee, a redirected return, a
    /// recursive pair, anything that yields no pop count - rebases the offset exactly like before
    /// the rule existed.</summary>
    [Fact]
    public void UnprovedCallStillRebases() {
        (IrMethod method, IrBlock block, IrStatement call) = PushCallMethod();
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(IrSsa.Build(method)),
            _ => null);

        IrStackOffset? offset = slots.OffsetAtBlockEnd(block);
        offset?.Delta.Should().Be(0);
        offset?.Base.Should().BeSameAs(call, "a refused call rebases on itself");
    }

    /// <summary>A proved <c>ret 0</c> callee: the return address the call pushed and popped cancel
    /// out, so the offset keeps its base at the pre-call delta.</summary>
    [Fact]
    public void ProvedCallPopZeroKeepsDelta() {
        (IrMethod method, IrBlock block, IrStatement call) = PushCallMethod();
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(IrSsa.Build(method)),
            statement => statement == call ? 0 : null);

        IrStackOffset? offset = slots.OffsetAtBlockEnd(block);
        offset?.Delta.Should().Be(-2);
        offset?.Base.Should().NotBeSameAs(call);
    }

    /// <summary>A proved <c>ret n</c> callee advances the offset by <c>n</c>: the push's -2 plus
    /// the pop count leaves the offset back at entry delta 0.</summary>
    [Fact]
    public void ProvedCallPopNAdvancesDelta() {
        (IrMethod method, IrBlock block, IrStatement call) = PushCallMethod();
        IrStackSlots slots = IrStackSlots.Compute(method, new IrMemoryAliasAnalysis(IrSsa.Build(method)),
            statement => statement == call ? 2 : null);

        slots.OffsetAtBlockEnd(block)?.Delta.Should().Be(0);
    }

    /// <summary>One block: a 16-bit push (delta -2 on the entry base), a call-shaped full memory
    /// boundary, then a return.</summary>
    private static (IrMethod Method, IrBlock Block, IrStatement Call) PushCallMethod() {
        CfgInstruction instruction = Instruction(0);
        IrBlock block = new(0);
        IrEditor.WithoutSsa.Append(block, IrStatement.Create(
            new StackCallNode(StackOperation.Push, BitWidth.WORD_16,
                new ConstantNode(DataType.UINT16, 0x1234)), instruction));
        IrStatement call = new(new CommentNode("call"), instruction,
            new IrEffects(CpuValues.None, CpuValues.None, CpuValues.Registers,
                IrTokens.None, IrTokens.Memory, false, false));
        IrEditor.WithoutSsa.Append(block, call);
        IrEditor.WithoutSsa.SetTerminator(block,
            new IrExit(instruction, new ReturnNode(new ConstantNode(DataType.UINT16, 0))));
        IrMethod method = new("M", [block], [new IrEntry(instruction, block)],
            new HashSet<ICfgNode> { instruction }, new Dictionary<ICfgNode, IrInstructionShape>());
        return (method, block, call);
    }

    private static CfgInstruction Instruction(ushort offset) =>
        CfgTestHelpers.CreateInstruction(new SegmentedAddress(0x1000, offset));
}
