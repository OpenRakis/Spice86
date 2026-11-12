namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Formatting tests of the line that reports the final-state difference of two runs.</summary>
public sealed class GeneratedCodeDifferentialReportTest {
    /// <summary>Equal RAM and equal extended regions are reported as identical.</summary>
    [Fact]
    public void DescribeReportsIdenticalWhenRamIsEqual() {
        // Arrange
        AsmFinalState emulated = BuildState(new byte[16]);
        AsmFinalState generated = BuildState(new byte[16]);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Be("k | strict | identical");
    }

    /// <summary>A RAM difference is summarised as ranges.</summary>
    [Fact]
    public void DescribeListsDifferentRamRanges() {
        // Arrange
        byte[] emulatedRam = new byte[16];
        byte[] generatedRam = new byte[16];
        generatedRam[2] = 1;
        generatedRam[3] = 1;
        generatedRam[7] = 1;
        AsmFinalState emulated = BuildState(emulatedRam);
        AsmFinalState generated = BuildState(generatedRam);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Be(
            "k | strict | ram: 3 bytes in 2 ranges: " +
            "0x00002-0x00003, 0x00007-0x00007 | extended: none");
    }

    /// <summary>Only the first ten RAM ranges are written, followed by an ellipsis.</summary>
    [Fact]
    public void DescribeWritesOnlyTheFirstTenRanges() {
        // Arrange
        byte[] emulatedRam = new byte[32];
        byte[] generatedRam = new byte[32];
        for (int i = 0; i < generatedRam.Length; i += 2) {
            generatedRam[i] = 1;
        }
        AsmFinalState emulated = BuildState(emulatedRam);
        AsmFinalState generated = BuildState(generatedRam);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Contain(", ... |");
        CountRanges(line).Should().Be(10, "only the first ten ranges must be written");
    }

    /// <summary>A differing extended region is named with its byte ranges.</summary>
    [Fact]
    public void DescribeListsExtendedRegionDifferences() {
        // Arrange
        byte[] emulatedXms = new byte[8];
        byte[] generatedXms = new byte[8];
        generatedXms[5] = 0xFF;
        AsmFinalState emulated = BuildState(new byte[16])
            with { ExtendedRegions = [("xms", emulatedXms)] };
        AsmFinalState generated = BuildState(new byte[16])
            with { ExtendedRegions = [("xms", generatedXms)] };

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Contain("extended: xms: 1 bytes in 1 ranges: 0x00005-0x00005");
    }

    /// <summary>The strict check passes when the compared states are equal and fails on a RAM
    /// difference.</summary>
    [Fact]
    public void CheckFailsOnAnyDifference() {
        // Arrange
        AsmFinalState emulated = BuildState(new byte[16]);

        // Act + Assert: equal states pass.
        GeneratedCodeDifferentialReport.Check("check-pass", emulated,
            BuildState(new byte[16]));

        byte[] wrongRam = new byte[16];
        wrongRam[9] = 1;
        Action wrongByte = () => GeneratedCodeDifferentialReport.Check("check-ram", emulated,
            BuildState(wrongRam));
        wrongByte.Should().Throw<Exception>()
            .WithMessage("*check-ram*ram: 1 bytes*");
    }

    /// <summary>A scenario skipped in generated mode needs a reason.</summary>
    [Fact]
    public void SkippedScenarioNeedsAReason() {
        AsmScenario scenario = AsmScenario.FromFixture("never-generated",
            () => AsmFixtureCatalog.Mul());
        AsmScenario marked = scenario.WithoutGeneratedExecution("time dependent");

        marked.GeneratedExecutionSkipReason.Should().Be("time dependent");
        Action empty = () => scenario.WithoutGeneratedExecution(" ");
        empty.Should().Throw<ArgumentException>();
    }

    /// <summary>A byte the generated run corrupted outside the scenario's result region fails the
    /// strict gate.</summary>
    [Fact]
    public void RealGateFailsOnInjectedByte() {
        AsmFixture fixture = AsmFixtureCatalog.Mul();
        GeneratedCodeMachineTestRunner runner = new();
        AsmFinalState interpreted;
        using (AsmRunScope interpretedScope = new(fixture)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            interpreted = AsmFinalState.Capture(interpretedRun, fixture.Settings);
        }

        AsmFinalState generated;
        using (AsmRunScope discoveryScope = new(fixture)) {
            GeneratedCSharpProgram program = runner.GenerateProgramAndSource(discoveryScope);
            using CompiledGeneratedOverride compiled =
                runner.CompileGeneratedProgram(fixture.Settings, program);
            using AsmRunScope generatedScope = new(fixture);
            using AsmFixtureRun generatedRun =
                AsmFixtureRunner.RunWithOverride(generatedScope, compiled.Supplier);
            generated = AsmFinalState.Capture(generatedRun, fixture.Settings);
        }

        GeneratedCodeDifferentialReport.Check("mul-gate", interpreted, generated);

        byte[] corrupted = (byte[])generated.Ram.Clone();
        corrupted[^1] ^= 0xFF;
        Action wrongByte = () => GeneratedCodeDifferentialReport.Check("mul-byte", interpreted,
            generated with { Ram = corrupted });
        wrongByte.Should().Throw<Exception>().WithMessage("*mul-byte*ram: 1 bytes*0xFFFFF*");
    }

    /// <summary>Final registers are not part of the compared state: mutating every register of the
    /// generated machine before the capture does not fail the gate.</summary>
    [Fact]
    public void RealGateIgnoresFinalRegisterDifferences() {
        AsmFixture fixture = AsmFixtureCatalog.Mul();
        GeneratedCodeMachineTestRunner runner = new();
        AsmFinalState interpreted;
        using (AsmRunScope interpretedScope = new(fixture)) {
            using AsmFixtureRun interpretedRun =
                AsmFixtureRunner.RunEmulated(interpretedScope, JitMode.InterpretedOnly);
            interpreted = AsmFinalState.Capture(interpretedRun, fixture.Settings);
        }

        AsmFinalState generated;
        using (AsmRunScope discoveryScope = new(fixture)) {
            GeneratedCSharpProgram program = runner.GenerateProgramAndSource(discoveryScope);
            using CompiledGeneratedOverride compiled =
                runner.CompileGeneratedProgram(fixture.Settings, program);
            using AsmRunScope generatedScope = new(fixture);
            using AsmFixtureRun generatedRun =
                AsmFixtureRunner.RunWithOverride(generatedScope, compiled.Supplier);
            generatedRun.Machine.CpuState.EAX ^= 0xFF;
            generatedRun.Machine.CpuState.EBX ^= 0xFF;
            generatedRun.Machine.CpuState.ESP ^= 0xFF;
            generatedRun.Machine.CpuState.Flags.FlagRegister ^= 0xFFFF;
            generated = AsmFinalState.Capture(generatedRun, fixture.Settings);
        }

        GeneratedCodeDifferentialReport.Check("mul-registers-ignored", interpreted, generated);
    }

    /// <summary>Every catalogue scenario marked not run in generated mode has a non-empty reason, and
    /// a run of a skipped scenario records that reason in the report while still generating source.</summary>
    [Fact]
    public void EverySkippedScenarioHasAReasonAndAppearsInTheReport() {
        foreach (AsmScenario scenario in AsmScenarioCatalog.All) {
            if (scenario.GeneratedExecutionSkipReason is { } reason) {
                reason.Should().NotBeNullOrWhiteSpace(
                    $"{scenario.Identity} is skipped in generated mode without a reason");
            }
        }

        AsmScenario skipped = AsmScenarioCatalog.All.First(
            scenario => scenario.Identity == "externalint");
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(skipped);
        string report = File.ReadAllText(Path.Join(AppContext.BaseDirectory,
            "generated-code", "differential.txt"));
        report.Should().Contain(
            $"externalint.pit.spec | skipped | {skipped.GeneratedExecutionSkipReason}");
    }

    private static AsmFinalState BuildState(byte[] ram) {
        return new AsmFinalState(ram, []);
    }

    private static int CountRanges(string line) {
        return line.Split("-0x").Length - 1;
    }
}
