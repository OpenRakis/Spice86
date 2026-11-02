namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.StateSerialization;
using Spice86.Shared.Interfaces;

using Spice86.Tests.CfgCpu;

using Xunit;

using static Spice86.Tests.CfgCodeGeneration.Ir.IrTestPrograms;

/// <summary>
/// The generator reports one CodeMethods step per emitted method through
/// <see cref="DumpProgressReporter"/>.
/// </summary>
public class GeneratorProgressTest {
    [Fact]
    public void GenerateReportsCodeMethodsStepsOneToMethodCount() {
        TestInstructionHelper helper = new();
        CfgInstruction first = Parse(helper, 0x0000, 0x90);
        CfgInstruction ret = Parse(helper, 0x0001, 0xC3);
        Chain(first, ret);
        CfgBlock block = Block(1, first, ret);
        CfgPartitionedProgram program = new() {
            Partitions = [Partition(1, block)],
            Transfers = []
        };
        DumpProgressReporter reporter = new(NullLogger.Instance, new SystemTimeProvider());
        List<(DumpPhase Phase, int Current, int Total, string Item)> steps = new();
        reporter.StepReported += (phase, current, total, item) => steps.Add((phase, current, total, item));

        new CfgCSharpGenerator().Generate(program, reporter);

        steps.Should().NotBeEmpty();
        steps.Should().OnlyContain(s => s.Phase == DumpPhase.CodeMethods);
        steps.Select(s => s.Current).Should().Equal(Enumerable.Range(1, steps.Count));
        steps.Select(s => s.Total).Distinct().Should().Equal(steps.Count);
        steps.Select(s => s.Item).Should().Equal("test_F000_0000_F0000");
    }

    [Fact]
    public void GenerateReportsTheMethodStepBeforeEmittingIt() {
        // Arrange: the first method's block jumps into the second partition, which has no transfer
        // metadata; the transfer emitter throws while lifting that method.
        TestInstructionHelper helper = new();
        CfgInstruction jmp = Parse(helper, 0x0000, 0xE9, 0xFD, 0x00);
        CfgInstruction target = Parse(helper, 0x0100, 0x90);
        CfgInstruction ret = Parse(helper, 0x0101, 0xC3);
        jmp.Successors.Add(target);
        jmp.SuccessorsPerType[InstructionSuccessorType.Normal] = new HashSet<ICfgNode> { target };
        Chain(target, ret);
        CfgCodePartition main = Partition(1, Block(1, jmp));
        CfgCodePartition other = Partition(2, Block(2, target, ret));
        CfgPartitionedProgram program = new() {
            Partitions = [main, other],
            Transfers = []
        };
        DumpProgressReporter reporter = new(NullLogger.Instance, new SystemTimeProvider());
        List<(DumpPhase Phase, int Current, int Total, string Item)> steps = new();
        reporter.StepReported += (phase, current, total, item) => steps.Add((phase, current, total, item));

        // Act
        Action generate = () => new CfgCSharpGenerator().Generate(program, reporter);

        // Assert: the step for the first method was already reported when its emission threw.
        generate.Should().Throw<NotSupportedException>()
            .WithMessage("Missing partition transfer metadata*");
        steps.Should().Equal((DumpPhase.CodeMethods, 1, 2, "test_F000_0000_F0000"));
    }

    [Fact]
    public void SlowMethodLineNamesEveryMeasuredStepInOrder() {
        GenerationStepTimer timer = new();
        timer.Record("lift", 5);
        timer.Record("ssa build 1", 1200);
        timer.Record("printing", 7);

        string line = CfgCSharpGenerator.SlowMethodLine(3, 7, "test_F000_0000_F0000", 30, 4, 80, timer);

        line.Should().Be("Code generation: method 3 / 7 test_F000_0000_F0000 took 1212 ms " +
            "(30 instructions, 4 IR blocks, 80 statements): lift 5 ms, ssa build 1 1200 ms, printing 7 ms");
    }
}
