namespace Spice86.Tests.AsmFixtures;

using FluentAssertions;

using Microsoft.Extensions.Logging;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Platform;
using Spice86.Tests.Utility;

/// <summary>Original hardware probes for the Tandy paths observed in OutRun.</summary>
internal static class TandyScenarioCatalog {
    private const string TestPortsObservation = "tandyTestPorts";

    /// <summary>Firmware detection on both platforms, native mode 09h, and native sound DMA.</summary>
    public static IReadOnlyList<AsmScenario> Scenarios => [
        Program("tandy_detection", MachineType.Pc, AssertPcDetection),
        Program("tandy_detection", MachineType.Tandy1000, AssertTandyDetection),
        Program("tandy_mode09", MachineType.Tandy1000, AssertMode09),
        Program("tandy_sound", MachineType.Tandy1000, AssertSound)
    ];

    private static AsmScenario Program(string identity, MachineType machineType,
        Action<AsmFixtureResult, TestIoPortHandler> assertResult) {
        return new AsmScenario(identity, scope => {
            string directory = scope.CreateWorkingDirectory(identity);
            string fileName = identity + ".com";
            string source = Path.Join(AppContext.BaseDirectory, "Resources", "TandyTests", fileName);
            string target = Path.Join(directory, fileName);
            File.Copy(source, target);
            AsmRunSettings settings = new() {
                BinName = target,
                MachineType = machineType,
                ScenarioName = identity,
                InstallInterruptVectors = true,
                FailOnUnhandledPort = true,
                RenderingMode = RenderingMode.Sync,
                MaxCycles = 500000,
                ConfigureMachine = machine => scope.Register(TestPortsObservation,
                    new TestIoPortHandler(machine.CpuState, Substitute.For<ILogger>(), machine.IoPortDispatcher))
            };
            return new AsmFixture(settings, [], result => {
                TestIoPortHandler ports = result.Observations.Get<TestIoPortHandler>(TestPortsObservation);
                ports.Results.Should().Equal([0], $"{identity} must complete every hardware check");
                assertResult(result, ports);
            });
        }, [$"TandyTests/{identity}.com"]);
    }

    private static void AssertPcDetection(AsmFixtureResult result, TestIoPortHandler ports) {
        ports.Details.Should().Equal([0], "the default PC must not advertise native Tandy hardware");
        result.Memory.UInt16[0x413].Should().Be(640);
    }

    private static void AssertTandyDetection(AsmFixtureResult result, TestIoPortHandler ports) {
        ports.Details.Should().Equal([1], "both ROM signatures and INT 15h/C0h must identify Tandy");
        result.Memory.UInt16[0x413].Should().Be(624);
        result.Memory.UInt16[0x415].Should().Be(640);
    }

    private static void AssertMode09(AsmFixtureResult result, TestIoPortHandler ports) {
        ports.Details.Should().Equal((byte)9);
        result.Memory.UInt8[0x449].Should().Be(9);
        result.Memory.UInt8[0x48A].Should().Be(0xF6, "raw page writes do not update the BIOS shadow");
        result.Memory.UInt8[0x98000].Should().Be(0x12);
        result.Memory.UInt8[0x80000].Should().Be(0xEF, "the CPU bank must differ from the display bank");
        uint[] pixels = result.CaptureVideoFrame();
        result.VideoRenderer.Width.Should().Be(320);
        result.VideoRenderer.Height.Should().Be(200);
        pixels.Take(2).Should().Equal(0xFFAA0000U, 0xFFAA5500U);
        pixels.Skip(320).Take(2).Should().Equal(0xFF00AAAAU, 0xFFAA0000U);
        pixels.Skip(640).Take(2).Should().Equal(0xFFAA00AAU, 0xFFAA5500U);
        pixels.Skip(960).Take(2).Should().Equal(0xFFAAAAAAU, 0xFF555555U);
        pixels.Skip(1280).Take(4).Should().Equal(0xFF55FF55U, 0xFF55FFFFU, 0xFFFFFF55U, 0xFF000000U);
    }

    private static void AssertSound(AsmFixtureResult result, TestIoPortHandler ports) {
        ports.Details.Should().Equal([0xC4, 1],
            "native BIOS sound must expose C4h and DMA terminal count must deliver exactly one IRQ 7");
        result.Memory.UInt16[0x40, 0xD0].Should().Be(0,
            "direct hardware playback must not start a BIOS-owned transfer");
    }
}
