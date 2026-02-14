namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

[Trait("Category", "Sound")]
public class SoundIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// Tests that the SB DSP reset produces 0xAA after an expected delay coming from real hardware behavior.
    /// </summary>
    [Fact]
    public void SoundBlasterDspReset_ProducesAA_AfterHardwareDelay() {
        // Arrange
        string comPath = Path.Combine("Resources", "Sound", "sb_reset_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        // Act
        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
            sbType: SbType.SBPro2);

        // Assert
        testHandler.Results.Should().Contain(0x00, "DSP reset should succeed with 0xAA");
        testHandler.Results.Should().NotContain(0xFF, "DSP reset should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2, "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().BeGreaterThan(0, "reset delay should require multiple poll iterations (20us hardware delay)");
    }

    private SoundTestHandler RunSoundTest(byte[] program, bool enablePit,
        long maxCycles, SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.GetFullPath($"{unitTestName}.com");
        File.WriteAllBytes(filePath, program);

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: filePath,
            enablePit: enablePit,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: true,
            sbType: sbType,
            oplMode: oplMode
        ).Create();

        SoundTestHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    private class SoundTestHandler : DefaultIOPortHandler {
        public List<byte> Results { get; } = new();
        public List<byte> Details { get; } = new();

        public SoundTestHandler(State state, ILoggerService loggerService,
            IOPortDispatcher ioPortDispatcher) : base(state, true, loggerService) {
            ioPortDispatcher.AddIOPortHandler(ResultPort, this);
            ioPortDispatcher.AddIOPortHandler(DetailsPort, this);
        }

        public override void WriteByte(ushort port, byte value) {
            if (port == ResultPort) {
                Results.Add(value);
            } else if (port == DetailsPort) {
                Details.Add(value);
            }
        }
    }
}




