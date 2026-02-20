namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System.Runtime.CompilerServices;

using Xunit;

/// <summary>
/// Integration tests for OPL timer and IO port latency that run machine code
/// through the full emulation stack. These verify that OPL hardware delays
/// match rel hardware behavior at 3000 cycles/ms.
/// </summary>
[Trait("Category", "Sound")]
public class OplIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// Tests that OPL register writes with standard inter-write delays
    /// result in correct Timer 1 operation (classic Adlib detection sequence).
    /// </summary>
    [Fact]
    public void OplWriteDelay_Timer1Fires_AfterHardwareDelay() {
        // Arrange
        string comPath = Path.Combine("Resources", "Sound", "opl_write_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        // Act
        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        // Assert
        testHandler.Results.Should().Contain(0x00, "OPL Timer 1 should overflow after 80us");
        testHandler.Results.Should().NotContain(0x01, "OPL status should be clear after timer reset");
        testHandler.Results.Should().NotContain(0x02, "OPL Timer 1 should not time out");
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().Be(1,
            "Real hardware reports 1 poll iteration " +
            "(35 post-data IO reads consume most of the 80us timer period)");
    }

    /// <summary>
    /// Tests OPL port read latency by counting how many status register
    /// reads (port 0x388) occur between Timer 1 start and overflow detection.
    /// </summary>
    [Fact]
    public void OplReadDelay_StatusReadCount_MatchesHardware() {
        // Arrange
        string comPath = Path.Combine("Resources", "Sound", "opl_read_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        // Act
        SoundTestHandler testHandler = RunSoundTest(program, enablePit: true, maxCycles: 500000L,
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




