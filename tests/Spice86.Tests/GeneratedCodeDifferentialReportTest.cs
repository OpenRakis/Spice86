namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;

using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Formatting tests of the line that reports the final-state difference of two runs.</summary>
public sealed class GeneratedCodeDifferentialReportTest {
    /// <summary>Equal registers and equal RAM are reported as identical.</summary>
    [Fact]
    public void DescribeReportsIdenticalWhenRegistersAndRamAreEqual() {
        // Arrange
        AsmFinalState emulated = BuildState(0x1234, 0x5678, new byte[16]);
        AsmFinalState generated = BuildState(0x1234, 0x5678, new byte[16]);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Be("k | strict | identical");
    }

    /// <summary>Differing registers are listed by name and the RAM difference is summarised as ranges.</summary>
    [Fact]
    public void DescribeListsDifferentRegistersAndRamRanges() {
        // Arrange
        byte[] emulatedRam = new byte[16];
        byte[] generatedRam = new byte[16];
        generatedRam[2] = 1;
        generatedRam[3] = 1;
        generatedRam[7] = 1;
        AsmFinalState emulated = BuildState(0x1234, 0x5678, emulatedRam);
        AsmFinalState generated = BuildState(0x5678, 0x5678, generatedRam);

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Be(
            "k | strict | registers: AX 0x00001234->0x00005678 | ram: 3 bytes in 2 ranges: " +
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
        AsmFinalState emulated = BuildState(0x1234, 0x5678, emulatedRam);
        AsmFinalState generated = BuildState(0x1234, 0x5678, generatedRam);

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
        AsmFinalState emulated = BuildState(0x1234, 0x5678, new byte[16])
            with { ExtendedRegions = [("xms", emulatedXms)] };
        AsmFinalState generated = BuildState(0x1234, 0x5678, new byte[16])
            with { ExtendedRegions = [("xms", generatedXms)] };

        // Act
        string line = GeneratedCodeDifferentialReport.Describe("k",
            GeneratedCodeDifferentialReport.StrictPolicy, emulated, generated);

        // Assert
        line.Should().Contain("extended: xms: 1 bytes in 1 ranges: 0x00005-0x00005");
    }

    /// <summary>The strict check passes when the final states are equal and fails otherwise.</summary>
    [Fact]
    public void CheckFailsOnAnyDifference() {
        // Arrange
        AsmFinalState emulated = BuildState(0x1234, 0x5678, new byte[16]);

        // Act + Assert: equal states pass.
        GeneratedCodeDifferentialReport.Check("check-pass", emulated,
            BuildState(0x1234, 0x5678, new byte[16]));

        byte[] wrongRam = new byte[16];
        wrongRam[9] = 1;
        Action wrongByte = () => GeneratedCodeDifferentialReport.Check("check-ram", emulated,
            BuildState(0x1234, 0x5678, wrongRam));
        wrongByte.Should().Throw<Exception>()
            .WithMessage("*check-ram*ram: 1 bytes*");

        Action wrongRegister = () => GeneratedCodeDifferentialReport.Check("check-reg", emulated,
            BuildState(0x1234, 0x9999, new byte[16]));
        wrongRegister.Should().Throw<Exception>()
            .WithMessage("*check-reg*BX 0x00005678->0x00009999*");
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
    /// strict gate, and so does a wrong final register.</summary>
    [Fact]
    public void RealGateFailsOnInjectedByteAndRegister() {
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

        List<(string Name, uint Value)> registers = [.. generated.Registers];
        int eax = registers.FindIndex(entry => entry.Name == "EAX");
        registers[eax] = ("EAX", registers[eax].Value ^ 0xFF);
        Action wrongRegister = () => GeneratedCodeDifferentialReport.Check("mul-reg", interpreted,
            generated with { Registers = registers });
        wrongRegister.Should().Throw<Exception>().WithMessage("*mul-reg*EAX*");
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

    private static AsmFinalState BuildState(ushort ax, ushort bx, byte[] ram) {
        (string Name, uint Value)[] registers = [("AX", ax), ("BX", bx)];
        return new AsmFinalState(registers, ram, []);
    }

    private static int CountRanges(string line) {
        return line.Split("-0x").Length - 1;
    }
}
