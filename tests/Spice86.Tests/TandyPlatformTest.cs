namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.InterruptHandlers.Bios;
using Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.Platform;
using Spice86.Tests.AsmFixtures;

using Xunit;

/// <summary>Runs the native Tandy probes through both CPU execution engines.</summary>
public sealed class TandyPlatformTest {
    [Theory]
    [InlineData(0x83, true)]
    [InlineData(0x84, true)]
    [InlineData(0x93, true)]
    [InlineData(0x94, false)]
    public void SoundBiosBufferProtectsRoutineWritesOnPc(ushort offset, bool overlapsBuffer) {
        // Arrange
        Memory memory = new(new(), new Ram(0x100000), new A20Gate(), new RealModeMmu386(), false);
        MemoryWriter writer = new(memory, new(0xFA00, offset));
        TandySoundBios.ReserveSoundBuffer(writer);

        // Act
        Action write = () => writer.WriteUInt16(0x1234);

        // Assert
        if (overlapsBuffer) {
            write.Should().Throw<InvalidOperationException>().WithMessage("*overlaps reserved firmware memory*");
            memory.UInt16[0xFA00, offset].Should().Be(0);
        } else {
            write.Should().NotThrow();
            memory.UInt16[0xFA00, offset].Should().Be(0x1234);
        }
    }

    [Fact]
    public void InitialTextModePreservesBiosPageBanks() {
        // Arrange
        AsmRunSettings settings = new() {
            BinName = Path.Join(AppContext.BaseDirectory, "Resources", "TandyTests", "tandy_detection.com"),
            MachineType = MachineType.Tandy1000,
            InstallInterruptVectors = true,
            RenderingMode = RenderingMode.Sync
        };
        using Spice86Creator creator = new(settings, JitMode.InterpretedOnly, null);

        // Act
        using Spice86DependencyInjection emulator = creator.Create();

        // Assert
        emulator.Machine.Memory.UInt8[0x40, 0x8A].Should().Be(0x3F);
    }

    public static IEnumerable<object[]> Cases =>
        TandyScenarioCatalog.Scenarios.SelectMany(scenario => new[] {
            new object[] { scenario.Identity, PlatformOf(scenario), JitMode.InterpretedOnly },
            new object[] { scenario.Identity, PlatformOf(scenario), JitMode.CompiledOnly }
        });

    [Theory]
    [MemberData(nameof(Cases))]
    public void HardwareProbeCompletes(string identity, string platform, JitMode jitMode) {
        // Arrange
        AsmScenario scenario = TandyScenarioCatalog.Scenarios.Single(candidate =>
            candidate.Identity == identity && PlatformOf(candidate) == platform);
        using AsmRunScope scope = scenario.Prepare();

        // Act
        using AsmFixtureRun run = AsmFixtureRunner.RunEmulated(scope, jitMode);

        // Assert
        string key = MachineGoldens.GoldenKey(scope.Fixture);
        MachineGoldens.CompareListingWithExpected(key, run.Machine);
        MachineGoldens.CompareCfgBlocksJsonWithExpected(key, run.Machine);
    }

    private static string PlatformOf(AsmScenario scenario) {
        using AsmRunScope scope = scenario.Prepare();
        return scope.Fixture.Settings.MachineType.ToString();
    }
}