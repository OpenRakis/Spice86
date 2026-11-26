namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

using Spice86.Tests.AsmFixtures;

using System.Collections.Immutable;

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

    /// <summary>A differing console output fails the gate, naming the key and the console part.</summary>
    [Fact]
    public void DifferentConsoleOutputFails() {
        AsmFinalState emulated = BuildState(new byte[16]) with { ConsoleOutput = "ok\n" };
        AsmFinalState generated = BuildState(new byte[16]) with { ConsoleOutput = "oK\n" };

        Action check = () => CheckFull("check-console", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-console*console*");
    }

    /// <summary>A differing drive file fails the gate, naming the key and the files part.</summary>
    [Fact]
    public void DifferentDriveFileFails() {
        AsmFinalState emulated = BuildState(new byte[16])
            with { DriveFiles = [("out.txt", new byte[] { 1, 2 })] };
        AsmFinalState generated = BuildState(new byte[16])
            with { DriveFiles = [("out.txt", new byte[] { 1, 3 })] };

        Action check = () => CheckFull("check-files", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-files*files*");
    }

    /// <summary>A differing device register fails the gate, naming the key, the device and the
    /// register.</summary>
    [Fact]
    public void DifferentDeviceRegisterFails() {
        AsmFinalState emulated = BuildState(new byte[16])
            with { DeviceRegisters = [("vga", "sequencer[02]", 0x0Fu)] };
        AsmFinalState generated = BuildState(new byte[16])
            with { DeviceRegisters = [("vga", "sequencer[02]", 0x03u)] };

        Action check = () => CheckFull("check-devices", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-devices*devices*");
    }

    /// <summary>A differing device state fails the gate, naming the key and the device.</summary>
    [Fact]
    public void DifferentDeviceStateFails() {
        MachineDeviceState emulatedState = new(new DualPicState(default, default, false), null, null,
            null, null, null, null, null, null, null, null);
        MachineDeviceState generatedState = emulatedState with {
            Pic = emulatedState.Pic!.Value with { IrqCheck = !emulatedState.Pic.Value.IrqCheck }
        };
        AsmFinalState emulated = BuildState(new byte[16]) with { DeviceState = emulatedState };
        AsmFinalState generated = BuildState(new byte[16]) with { DeviceState = generatedState };

        Action check = () => CheckFull("check-devstate", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-devstate*pic*");
    }

    /// <summary>One differing byte of VGA video memory fails the gate, naming the key, the device and
    /// the video-memory path: the screen lives in VGA memory, not in the compared RAM.</summary>
    [Fact]
    public void VgaVideoMemoryDifferenceFails() {
        byte[] emulatedVRam = new byte[0x40000];
        byte[] generatedVRam = new byte[0x40000];
        generatedVRam[0x1234] = 0x41;
        MachineDeviceState emulatedState = EmptyDeviceState() with { Vga = BuildVgaState(emulatedVRam) };
        MachineDeviceState generatedState = EmptyDeviceState() with { Vga = BuildVgaState(generatedVRam) };
        AsmFinalState emulated = BuildState(new byte[16]) with { DeviceState = emulatedState };
        AsmFinalState generated = BuildState(new byte[16]) with { DeviceState = generatedState };

        Action check = () => CheckFull("check-vga", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-vga*vga*VRam*0x1234*");
    }

    /// <summary>A differing element of an immutable-array member fails the gate, naming the key, the
    /// device and the element path.</summary>
    [Fact]
    public void ImmutableArrayMemberDifferenceFails() {
        EmsState emulatedEms = BuildEmsState(0x00);
        EmsState generatedEms = BuildEmsState(0xFF);
        MachineDeviceState emulatedState = EmptyDeviceState() with { Ems = emulatedEms };
        MachineDeviceState generatedState = EmptyDeviceState() with { Ems = generatedEms };
        AsmFinalState emulated = BuildState(new byte[16]) with { DeviceState = emulatedState };
        AsmFinalState generated = BuildState(new byte[16]) with { DeviceState = generatedState };

        Action check = () => CheckFull("check-ems", emulated, generated);

        check.Should().Throw<Exception>().WithMessage("*check-ems*ems*");
    }

    /// <summary>A VRAM byte difference of an execution that leaves the video memory out does not
    /// throw, and the report line names the part and the reason; a VGA register difference outside
    /// the video memory still throws.</summary>
    [Fact]
    public void UncomparedVideoMemoryIsNotCompared() {
        byte[] emulatedVRam = new byte[0x40000];
        byte[] generatedVRam = new byte[0x40000];
        generatedVRam[0x1234] = 0x41;
        MachineDeviceState emulatedState = EmptyDeviceState() with { Vga = BuildVgaState(emulatedVRam) };
        MachineDeviceState generatedState = EmptyDeviceState() with { Vga = BuildVgaState(generatedVRam) };
        AsmFinalState emulated = BuildState(new byte[16]) with { DeviceState = emulatedState };
        AsmFinalState generated = BuildState(new byte[16]) with { DeviceState = generatedState };
        FinalStateComparison comparison = new(null, FinalStateParts.VideoMemory,
            "the screen shows a count that depends on timing", []);

        string line = CheckFull("check-video-memory", emulated, generated, comparison);

        line.Should().Contain("video memory").And.Contain("the screen shows a count that depends on timing");

        MachineDeviceState registerDifference = generatedState with {
            Vga = generatedState.Vga! with {
                Registers = generatedState.Vga.Registers with { MiscellaneousOutput = 0xFF }
            }
        };
        Action check = () => CheckFull("check-video-register", emulated,
            generated with { DeviceState = registerDifference }, comparison);
        check.Should().Throw<Exception>().WithMessage("*check-video-register*vga*MiscellaneousOutput*");
    }

    private static MachineDeviceState EmptyDeviceState() => new(null, null, null, null, null, null,
        null, null, null, null, null);

    private static VgaState BuildVgaState(byte[] vram) {
        DacState dac = new(new byte[768], 0, 0, 0, 0, 0, false, 0);
        VgaRegistersState registers = new(0, new byte[5], 0, new byte[0x19], 0, new byte[9], 0,
            new byte[0x15], false, 0, 0, 0, 0, 0, 0, dac);
        return new VgaState(registers, new VideoMemoryState(vram, new byte[4]));
    }

    private static EmsState BuildEmsState(byte contentByte) {
        byte[] content = new byte[4];
        content[2] = contentByte;
        return new EmsState(
            ImmutableArray.Create(new EmmPageState(0, content)),
            ImmutableArray<EmmHandleState>.Empty,
            ImmutableArray<int>.Empty,
            ImmutableArray<int>.Empty);
    }

    /// <summary>A timing-dependent execution still compares device registers: a differing register
    /// fails the gate even though RAM is left out.</summary>
    [Fact]
    public void TimingDependentScenarioStillComparesDeviceRegisters() {
        AsmFinalState emulated = BuildState(new byte[16])
            with { DeviceRegisters = [("pit", "channel0.reload", 0u)] };
        AsmFinalState generated = BuildState(new byte[16])
            with { DeviceRegisters = [("pit", "channel0.reload", 0xFFFFu)] };

        Action check = () => CheckFull("check-timing-devices", emulated, generated,
            new FinalStateComparison("time dependent", FinalStateParts.None, "", []));

        check.Should().Throw<Exception>().WithMessage("*check-timing-devices*devices*");
    }

    /// <summary>A part the scenario leaves out of the comparison is not compared: a differing
    /// console does not throw, and the report line names the part and the reason.</summary>
    [Fact]
    public void UncomparedConsoleIsNotCompared() {
        AsmFinalState emulated = BuildState(new byte[16]) with { ConsoleOutput = "1\n" };
        AsmFinalState generated = BuildState(new byte[16]) with { ConsoleOutput = "2\n" };

        string line = CheckFull("check-uncompared", emulated, generated,
            new FinalStateComparison(null, FinalStateParts.Console,
                "the poll count depends on timing", []));

        line.Should().Contain("console").And.Contain("the poll count depends on timing");
    }

    /// <summary>A RAM range the scenario leaves out of the comparison is not compared: bytes that
    /// differ only inside the range do not throw, and the report line names the reason.</summary>
    [Fact]
    public void UncomparedRamRangeIsNotCompared() {
        byte[] emulatedRam = new byte[0x21010];
        byte[] generatedRam = new byte[0x21010];
        generatedRam[0x20FFE] = 0x02;
        generatedRam[0x20FFF] = 0x02;
        AsmFinalState emulated = BuildState(emulatedRam);
        AsmFinalState generated = BuildState(generatedRam);

        string line = CheckFull("check-uncompared-ram", emulated, generated,
            new FinalStateComparison(null, FinalStateParts.None, "",
                [(0x20FFEu, 2, "the pushed FLAGS word is not computed")]));

        line.Should().Contain("the pushed FLAGS word is not computed");
    }

    /// <summary>A byte differing outside the uncompared RAM range still fails the gate, naming the
    /// key and the ram part.</summary>
    [Fact]
    public void RamOutsideTheUncomparedRangeIsCompared() {
        byte[] emulatedRam = new byte[0x21010];
        byte[] generatedRam = new byte[0x21010];
        generatedRam[0x20FFE] = 0x02;
        generatedRam[0x21000] = 0xFF;
        AsmFinalState emulated = BuildState(emulatedRam);
        AsmFinalState generated = BuildState(generatedRam);

        Action check = () => CheckFull("check-outside-ram", emulated, generated,
            new FinalStateComparison(null, FinalStateParts.None, "",
                [(0x20FFEu, 2, "the pushed FLAGS word is not computed")]));

        check.Should().Throw<Exception>().WithMessage("*check-outside-ram*ram*");
    }

    /// <summary>A timing-dependent scenario needs a reason.</summary>
    [Fact]
    public void TimingDependentScenarioNeedsAReason() {
        AsmScenario scenario = AsmScenario.FromFixture("time-dependent",
            () => AsmFixtureCatalog.Mul());
        AsmScenario marked = scenario.WithTimingDependentState("time dependent");

        marked.TimingDependentReason.Should().Be("time dependent");
        Action empty = () => scenario.WithTimingDependentState(" ");
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

    /// <summary>A scenario marked timing-dependent still runs its oracle in generated mode: the oracle
    /// runs once for the interpreted discovery and once for the generated run.</summary>
    [Fact]
    public void TimingDependentScenarioStillRunsTheOracle() {
        int oracleCalls = 0;
        AsmScenario scenario = AsmScenarioCatalog.All
            .First(candidate => candidate.Identity == "check_stdin_status_ctrl_c")
            .WithOracle(fixture => new AsmFixture(fixture.Settings, fixture.ExpectedMemory, result => {
                oracleCalls++;
                fixture.AssertResult(result);
            }));

        new GeneratedCodeMachineTestRunner().TestGeneratedCode(scenario);

        oracleCalls.Should().Be(2, "the oracle runs for the interpreted discovery and for the generated run");
    }

    /// <summary>Every catalogue scenario marked timing-dependent has a non-empty reason, and a run of
    /// one records that reason in the report while still generating source and running the
    /// generated code.</summary>
    [Fact]
    public void EveryTimingDependentScenarioHasAReasonAndAppearsInTheReport() {
        foreach (AsmScenario scenario in AsmScenarioCatalog.All
                .Where(scenario => scenario.TimingDependentReason is not null)) {
            scenario.TimingDependentReason.Should().NotBeNullOrWhiteSpace(
                $"{scenario.Identity} is timing-dependent without a reason");
        }

        AsmScenario timingDependent = AsmScenarioCatalog.All.First(
            scenario => scenario.Identity == "externalint");
        new GeneratedCodeMachineTestRunner().TestGeneratedCode(timingDependent);
        string report = File.ReadAllText(Path.Join(AppContext.BaseDirectory,
            "generated-code", "differential.txt"));
        report.Should().Contain(
            $"externalint.pit.spec | timing-dependent | {timingDependent.TimingDependentReason}");
    }

    private static string CheckFull(string key, AsmFinalState emulated, AsmFinalState generated,
        FinalStateComparison? comparison = null) =>
        GeneratedCodeDifferentialReport.Check(key, GenerationPolicy.Strict, emulated, generated,
            new HashSet<SegmentedAddress>(), [], [], comparison ?? FinalStateComparison.Strict);

    private static AsmFinalState BuildState(byte[] ram) {
        return new AsmFinalState(ram, []);
    }

    private static int CountRanges(string line) {
        return line.Split("-0x").Length - 1;
    }
}
