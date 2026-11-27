namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model.Plan;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Naming;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Shared.Emulator.Memory;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Checks the exact register-write summaries the hardware interrupt effect builder computes for
/// recursive and non-recursive program calls.
/// </summary>
public sealed class HardwareInterruptEffectBuilderSummaryTest {
    private static readonly Lazy<CfgPartitionedProgram> Program = new(LoadProgram);

    [Fact]
    public void MutualRecursionRegisterArgumentHasExactWrites() {
        HardwareInterruptEffectBuilder builder = CreateBuilder(Program.Value);

        builder.CallTargetWrites(CallAt(0x009C)).Should().Be(CpuValues.Ax | CpuValues.Cx);
    }

    [Fact]
    public void MutualRecursionWithFramesHasExactWrites() {
        HardwareInterruptEffectBuilder builder = CreateBuilder(Program.Value);
        CpuValues expected = CpuValues.Ax | CpuValues.Sp | CpuValues.Bp;

        builder.CallTargetWrites(CallAt(0x00C9)).Should().Be(expected);
        builder.CallTargetWrites(CallAt(0x00E8)).Should().Be(expected);
    }

    [Fact]
    public void SelfRecursionHasExactWrites() {
        HardwareInterruptEffectBuilder builder = CreateBuilder(Program.Value);
        CpuValues forbidden = CpuValues.Ecx | CpuValues.Ebx | CpuValues.Esi | CpuValues.Edi
            | CpuValues.Es | CpuValues.Cs | CpuValues.Ds | CpuValues.Fs | CpuValues.Gs;

        foreach (ushort offset in new ushort[] { 0x005F, 0x0070 }) {
            CpuValues writes = builder.CallTargetWrites(CallAt(offset));
            writes.Should().HaveFlag(CpuValues.Ax, $"the call at F000:{offset:X4} writes AX");
            writes.Should().HaveFlag(CpuValues.Dx, $"the call at F000:{offset:X4} writes DX");
            (writes & forbidden).Should().Be(CpuValues.None,
                $"the call at F000:{offset:X4} does not write unrelated registers");
        }
    }

    [Fact]
    public void NonRecursiveCalleeIsUnchanged() {
        HardwareInterruptEffectBuilder builder = CreateBuilder(Program.Value);

        builder.CallTargetWrites(CallAt(0x008B)).Should().Be(CpuValues.Ax | CpuValues.Sp | CpuValues.Bp);
    }

    [Fact]
    public void SummariesDoNotDependOnQueryOrder() {
        ushort[] order = [0x005F, 0x00C9, 0x009C, 0x0070, 0x00E8];
        HardwareInterruptEffectBuilder forward = CreateBuilder(Program.Value);
        HardwareInterruptEffectBuilder reverse = CreateBuilder(Program.Value);

        foreach (ushort offset in order) {
            forward.CallTargetWrites(CallAt(offset));
        }
        foreach (ushort offset in order.Reverse()) {
            reverse.CallTargetWrites(CallAt(offset));
        }

        foreach (ushort offset in order) {
            forward.CallTargetWrites(CallAt(offset)).Should()
                .Be(reverse.CallTargetWrites(CallAt(offset)),
                    $"the call at F000:{offset:X4} gets the same writes in both query orders");
        }
    }

    private static CfgPartitionedProgram LoadProgram() {
        using AsmRunScope scope = new(AsmFixtureCatalog.Partition("cc_recursive_summary"));
        return GeneratedCodeMachineTestRunner.GenerateProgram(scope).Program;
    }

    private static HardwareInterruptEffectBuilder CreateBuilder(CfgPartitionedProgram program) {
        EffectBuilderSetup setup = EffectBuilderSetup.Create(program);
        setup.Effects.Compute();
        return setup.Effects;
    }

    private static CfgInstruction CallAt(ushort offset) {
        SegmentedAddress address = new(0xF000, offset);
        return Program.Value.Partitions
            .SelectMany(partition => partition.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<CfgInstruction>()
            .Single(instruction => instruction.Address == address);
    }
}
