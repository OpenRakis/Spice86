namespace Spice86.Tests.CfgCodeGeneration;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Data;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ssa;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Structuring;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Checks that the parallel preparation rounds produce byte-identical output and identical
/// diagnostics to the sequential rounds.
/// </summary>
public sealed class CfgCSharpGeneratorParallelismTest {
    [Theory]
    [InlineData("cc_recursive_summary")]
    [InlineData("cc_call_shapes")]
    [InlineData("cc_stack_frames")]
    [InlineData("cc_irq_delivered_flags")]
    [InlineData("stalestackframes")]
    [InlineData("partition_multi_entry_irreducible_shared")]
    public void ParallelGenerationIsDeterministic(string scenario) {
        CfgPartitionedProgram program;
        using (AsmRunScope scope = new(Fixture(scenario))) {
            program = GeneratedCodeMachineTestRunner.GenerateProgram(scope).Program;
        }
        GeneratedCSharpProgram sequential = Generate(program, 1);
        for (int run = 0; run < 5; run++) {
            GeneratedCSharpProgram parallel = Generate(program, 8);

            parallel.Files.Select(file => file.Name).Should().Equal(
                sequential.Files.Select(file => file.Name), $"file names match in run {run}");
            parallel.Files.Select(file => file.Text).Should().Equal(
                sequential.Files.Select(file => file.Text), $"file texts match in run {run}");
            parallel.Manifest.ToJson().Should().Be(sequential.Manifest.ToJson(),
                $"manifests match in run {run}");
            CensusKey(parallel).Should().Be(CensusKey(sequential), $"censuses match in run {run}");
        }
    }

    private static AsmFixture Fixture(string scenario) => scenario switch {
        "cc_irq_delivered_flags" => AsmFixtureCatalog.IrqDeliveredFlags(),
        "stalestackframes" => AsmFixtureCatalog.StaleStackFrames(),
        _ => AsmFixtureCatalog.Partition(scenario)
    };

    private static GeneratedCSharpProgram Generate(CfgPartitionedProgram program, int degree) =>
        new CfgCSharpGenerator(new GeneratorOptions {
            CollectCensuses = true,
            Verify = true,
            MaxDegreeOfParallelism = degree
        }).Generate(program);

    /// <summary>The census comparison key: numeric counters for the goto, web and flag-read
    /// censuses, and the sorted access/direction pairs of the constant memory access table.</summary>
    private static string CensusKey(GeneratedCSharpProgram program) {
        string gotos = string.Join("|", Enum.GetValues<GotoKeepReason>()
            .Select(reason => $"{reason}:{program.GotoCensus.Count(reason)}"));
        string flags = string.Join("|", Enum.GetValues<FlagSetterKind>()
            .Select(kind => $"k{kind}:{program.FlagReadCensus.Count(kind)}")
            .Concat(Enum.GetValues<FlagSetterDistance>()
                .Select(distance => $"d{distance}:{program.FlagReadCensus.Count(distance)}"))
            .Concat(Enum.GetValues<FlagPropagationBlocker>()
                .Select(blocker => $"b{blocker}:{program.FlagReadCensus.Count(blocker)}")));
        WebCensus web = program.WebCensus;
        string webs = string.Join(',', web.Webs, web.PureWebs, web.LoadOnlyWebs, web.StoreOnlyWebs,
            web.ThroughWebs, web.PureUses, web.LoadOnlyUses, web.StoreOnlyUses, web.ThroughUses,
            web.PureMultiBlockWebs);
        string memory = string.Join("|", program.ConstantMemoryAccesses.Entries.Select(entry =>
            $"{entry.Key.Segment}:{entry.Key.Offset:X4}:{entry.Key.Width}:{entry.Value}"));
        return string.Join('\n', gotos, flags, webs, memory);
    }
}
