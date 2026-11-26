namespace Spice86.Tests.Emulator.Memory;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Model;
using Spice86.Core.Emulator.ReverseEngineer.FunctionPartitioning.Model;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Emulator.VM.Breakpoint;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>
/// Checks that each memory write reaches the <see cref="IMemoryWriteObserver"/> once, with the width, address
/// and value it was issued with, before the byte breakpoints of its bytes, and that the interpreter and
/// generated code report the same writes.
/// </summary>
public sealed class MemoryWriteEventsTests {
    /// <summary>
    /// Logs the width events and, through an unconditional memory write breakpoint, the bytes, in the order
    /// they come: <c>W16 2FFFF=1234</c> for a write, <c>b 2FFFF=34</c> for one of its bytes.
    /// </summary>
    private sealed class Recorder : IMemoryWriteObserver {
        public List<MemoryWrite> Writes { get; } = [];

        public List<string> Lines { get; } = [];

        public void OnMemoryWrite(in MemoryWrite write) {
            Writes.Add(write);
            Lines.Add(Describe(write));
        }

        public void OnByte(uint address, byte value) => Lines.Add($"b {address:X5}={value:X2}");

        public static string Describe(MemoryWrite write) =>
            $"W{(int)write.Width} {write.Address:X5}={write.Value:X}";
    }

    private static AsmRunSettings Observed(AsmRunSettings settings, Recorder recorder) {
        Action<Machine> configure = settings.ConfigureMachine;
        return settings with {
            ConfigureMachine = machine => {
                configure(machine);
                machine.Memory.WriteEvents.Observer = recorder;
                IMemory memory = machine.Memory;
                machine.EmulatorBreakpointsManager.ToggleBreakPoint(new UnconditionalBreakPoint(
                    BreakPointType.MEMORY_WRITE,
                    (_, address) => recorder.OnByte((uint)address, memory.CurrentlyWritingByte), false), true);
            }
        };
    }

    private static (Recorder Emulated, Recorder Generated) RecordBothForms(string fixtureName,
            GeneratorOptions options) {
        using AsmRunScope discovery = new(AsmFixtureCatalog.Partition(fixtureName));
        AsmRunSettings settings = discovery.Fixture.Settings;
        CfgPartitionedProgram program = GeneratedCodeMachineTestRunner.GenerateProgram(discovery).Program;
        GeneratedCSharpProgram generated = new CfgCSharpGenerator(options with {
            CollectCensuses = true, Verify = true
        }).Generate(program);
        using CompiledGeneratedOverride compiled = new GeneratedOverrideCompiler().CompileSupplier(generated);
        Recorder emulatedRecorder = new();
        Recorder generatedRecorder = new();
        using AsmFixtureRun emulatedRun =
            AsmFixtureRunner.RunSettings(Observed(settings, emulatedRecorder), JitMode.InterpretedOnly);
        using AsmFixtureRun generatedRun =
            AsmFixtureRunner.RunSettingsWithOverride(Observed(settings, generatedRecorder), compiled.Supplier);
        return (emulatedRecorder, generatedRecorder);
    }

    private static Memory CreateMemory(IMmu mmu) {
        return new Memory(new AddressReadWriteBreakpoints(), new Ram(0x110000), new A20Gate(), mmu, false);
    }

    [Theory]
    [InlineData("dataflow_alias_traps")]
    [InlineData("instruction_coverage_386")]
    [InlineData("partition_far_call_chain")]
    [InlineData("int_service_calls")]
    [InlineData("dataflow_joined_memory")]
    public void CheckBuildReportsTheSameWritesAsTheInterpreter(string fixtureName) {
        // Act
        (Recorder emulated, Recorder generated) = RecordBothForms(fixtureName, GeneratorOptions.CheckBuild);

        // Assert
        emulated.Writes.Should().NotBeEmpty();
        generated.Lines.Should().Equal(emulated.Lines);
    }

    [Fact]
    public void EachWriteIsReportedOnceWithItsWidthBeforeItsBytes() {
        // Arrange
        Recorder recorder = new();
        AsmRunSettings settings = Observed(AsmFixtureCatalog.Partition("dataflow_alias_traps").Settings, recorder);

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunSettings(settings, JitMode.InterpretedOnly);

        // Assert: mov byte [0], 55h with DS = 2000h
        recorder.Lines.Should().ContainInConsecutiveOrder("W8 20000=55", "b 20000=55");
        // Assert: mov word [0FFFFh], 1234h wraps in the segment, its high byte goes to offset 0
        recorder.Lines.Should().ContainInConsecutiveOrder("W16 2FFFF=1234", "b 2FFFF=34", "b 20000=12");
        // Assert: every write is followed by one breakpoint per byte
        int writeIndex = 0;
        for (int index = 0; index < recorder.Lines.Count; index++) {
            if (recorder.Lines[index].StartsWith('W')) {
                int bytes = (int)recorder.Writes[writeIndex++].Width / 8;
                recorder.Lines.Skip(index + 1).Take(bytes).Should().HaveCount(bytes)
                    .And.OnlyContain(line => line.StartsWith("b "),
                        $"{recorder.Lines[index]} is followed by its bytes");
            }
        }
    }

    [Fact]
    public void RepStoswReportsOneWordWritePerRepetitionInBothForms() {
        // Act: AX = 5555h, ES:DI = 2000:0058h, CX = 5
        (Recorder emulated, Recorder generated) =
            RecordBothForms("register_locals_across", GeneratorOptions.CheckBuild);

        // Assert
        string[] expected = [
            "W16 20058=5555", "W16 2005A=5555", "W16 2005C=5555", "W16 2005E=5555", "W16 20060=5555"
        ];
        emulated.Writes.Select(Recorder.Describe).Should().ContainInConsecutiveOrder(expected);
        generated.Writes.Select(Recorder.Describe).Should().ContainInConsecutiveOrder(expected);
    }

    [Fact]
    public void DwordStoreIsReportedAsOneWriteInBothForms() {
        // Act: mov [bx+16h], edx with DS = 2000h, BX = 0 and EDX = 78563412h
        (Recorder emulated, Recorder generated) =
            RecordBothForms("instruction_coverage_386", GeneratorOptions.CheckBuild);

        // Assert
        emulated.Lines.Should().ContainInConsecutiveOrder("W32 20016=78563412",
            "b 20016=12", "b 20017=34", "b 20018=56", "b 20019=78");
        generated.Lines.Should().ContainInConsecutiveOrder("W32 20016=78563412",
            "b 20016=12", "b 20017=34", "b 20018=56", "b 20019=78");
    }

    [Fact]
    public void DefaultBuildReportsJoinedWordStoresAsOneDwordWrite() {
        // Act: mov [0044h], ax then mov [0046h], dx, with DS = 2000h and DX:AX = 89ABCDEFh
        (Recorder emulated, Recorder generated) =
            RecordBothForms("dataflow_joined_memory", GeneratorOptions.Default);

        // Assert: the interpreter reports two word writes, the default build one dword write of the same bytes
        emulated.Lines.Should().ContainInConsecutiveOrder("W16 20044=CDEF", "b 20044=EF", "b 20045=CD",
            "W16 20046=89AB", "b 20046=AB", "b 20047=89");
        generated.Lines.Should().ContainInConsecutiveOrder("W32 20044=89ABCDEF",
            "b 20044=EF", "b 20045=CD", "b 20046=AB", "b 20047=89");
        generated.Lines.Where(line => line.StartsWith("b ")).Should()
            .Equal(emulated.Lines.Where(line => line.StartsWith("b ")));
    }

    [Fact]
    public void WritesOutsideTheIndexersAreSplitOrNotReported() {
        // Arrange
        Memory memory = CreateMemory(new RealModeMmu8086());
        Recorder recorder = new();
        memory.WriteEvents.Observer = recorder;

        // Act
        memory.SegmentedAddress16[0x1000, 0x0010] = new SegmentedAddress(0xABCD, 0x1234);
        memory.UInt16BigEndian[0x200] = 0x1234;
        memory.WriteUInt32Segmented(0x1000, 0xFFFE, 0x11223344);
        memory[0x300] = 0x55;
        memory.SneakilyWrite(0x301, 0x66);

        // Assert
        recorder.Writes.Select(Recorder.Describe).Should().Equal(
            "W16 10010=1234", "W16 10012=ABCD", "W16 00200=3412", "W32 1FFFE=11223344");
    }

    [Fact]
    public void AWriteTheMmuRefusesIsNotReported() {
        // Arrange
        Memory memory = CreateMemory(new RealModeMmu386());
        Recorder recorder = new();
        memory.WriteEvents.Observer = recorder;

        // Act
        Action write = () => memory.UInt16[0x2000, 0xFFFFu] = 0x1234;

        // Assert
        write.Should().Throw<CpuGeneralProtectionFaultException>();
        recorder.Writes.Should().BeEmpty();
    }
}
