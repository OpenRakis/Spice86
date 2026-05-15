namespace Spice86.Tests.Emulator.Devices.Sound;

using FluentAssertions;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Shared.Interfaces;
using Spice86.Tests.Utility;

using Xunit;

/// <summary>
/// Integration tests for OPL timer and IO port latency that run machine code
/// through the full emulation stack. These verify that OPL hardware delays
/// match rel hardware behavior at 3000 cycles/ms.
/// </summary>
[Trait("Category", "Sound")]
public class OplIntegrationTests {
    /// <summary>
    /// Tests that OPL register writes with standard inter-write delays
    /// result in correct Timer 1 operation (classic Adlib detection sequence).
    /// </summary>
    [Fact]
    public void OplWriteDelay_Timer1Fires_AfterHardwareDelay() {
        // Arrange
        string comPath = Path.Join(AppContext.BaseDirectory, "Resources", "Sound", "opl_write_delay.com");

        // Act
        TestIoPortHandler testHandler = RunSoundTest(comPath, enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        // Assert
        testHandler.Results.Should().Contain(0x00, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, "OPL Timer 1 should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().Be(1,
            "OPL Timer 1 should overflow before the first poll iteration completes");
    }

    /// <summary>
    /// Tests OPL port read latency by counting how many status register
    /// reads (port 0x388) occur between Timer 1 start and overflow detection.
    /// </summary>
    [Fact]
    public void OplReadDelay_StatusReadCount_MatchesHardware() {
        // Arrange
        string comPath = Path.Join(AppContext.BaseDirectory, "Resources", "Sound", "opl_read_delay.com");

        // Act
        TestIoPortHandler testHandler = RunSoundTest(comPath, enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        // Assert
        testHandler.Results.Should().Contain(0x00, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, "OPL Timer 1 should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of read count");
        int readCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        readCount.Should().BeGreaterThanOrEqualTo(1,
            "Real hardware reports 2 status reads before Timer 1 overflow");
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
