namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

[Trait("Category", "Sound")]
public class SoundIntegrationTests {
    /// <summary>
    /// Tests that the SB DSP reset produces 0xAA after an expected delay coming from real hardware behavior.
    /// </summary>
    [Fact]
    public void SoundBlasterDspReset_ProducesAA_AfterHardwareDelay() {
        // Arrange
        string comPath = Path.Join(AppContext.BaseDirectory, "Resources", "Sound", "sb_reset_delay.com");

        // Act
        TestIoPortHandler testHandler = RunSoundTest(comPath, enablePit: true, maxCycles: 500000L,
            sbType: SbType.SBPro2);

        // Assert
        testHandler.Results.Should().Contain(0x00, "DSP reset should succeed with 0xAA");
        testHandler.Results.Should().NotContain(0xFF, "DSP reset should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2, "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().BeGreaterThan(0, "reset delay should require multiple poll iterations (20us hardware delay)");
    }

    private TestIoPortHandler RunSoundTest(string comPath, bool enablePit,
        long maxCycles, SbType sbType = SbType.None, OplMode oplMode = OplMode.None) {
        using Spice86Creator creator = new Spice86Creator(
            binName: comPath,
            enablePit: enablePit,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: true,
            sbType: sbType,
            oplMode: oplMode
        );
        using Spice86DependencyInjection spice86DependencyInjection = creator.Create();

        TestIoPortHandler testHandler = new(
            spice86DependencyInjection.Machine.CpuState,
            NSubstitute.Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

}
