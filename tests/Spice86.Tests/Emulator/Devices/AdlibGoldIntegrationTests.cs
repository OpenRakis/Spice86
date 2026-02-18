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
/// Integration tests for AdLib Gold initial audio delay. These verify that
/// OPL3Gold mode produces audio immediately upon key-on without excessive
/// startup latency. The test programs the AdLib Gold control interface and
/// OPL registers, then uses Timer 1 to verify timing matches standard OPL3.
/// </summary>
[Trait("Category", "Sound")]
public class AdlibGoldIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// Tests that OPL3Gold mode initializes and produces audio without an
    /// initial delay. The ASM test:
    /// 1. Activates AdLib Gold control interface (port 0x38A = 0xFF)
    /// 2. Verifies board options read (should return 0x50)
    /// 3. Sets stereo FM volumes to maximum
    /// 4. Programs OPL channel 0 with fast-attack envelope
    /// 5. Key-on channel 0
    /// 6. Starts Timer 1 and polls for overflow
    ///
    /// The test verifies that all events occur and Timer 1 fires within
    /// normal bounds, proving there is no excessive delay in the AdLib
    /// Gold rendering pipeline.
    /// </summary>
    [Fact]
    public void AdlibGold_NotePlayback_StartsWithoutInitialDelay() {
        // Arrange
        string comPath = Path.Combine("Resources", "Sound", "adlib_gold_init_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        // Act
        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3Gold);

        // Assert - Basic test completion
        testHandler.Results.Should().NotBeEmpty("the test program should report a result");
        testHandler.Results.Should().NotContain(0x01,
            "AdLib Gold control should respond with board options 0x50");
        testHandler.Results.Should().NotContain(0x02,
            "OPL Timer 1 should not time out");
        testHandler.Results.Should().Contain(0x00,
            "OPL Timer 1 should fire after key-on, proving audio pipeline is active");

        // Assert - Iteration count
        testHandler.Details.Should().HaveCountGreaterThanOrEqualTo(2,
            "should report low and high byte of poll iteration count");
        int iterationCount = testHandler.Details[0] | (testHandler.Details[1] << 8);
        iterationCount.Should().BeGreaterThanOrEqualTo(1,
            "timer poll should take at least 1 iteration");
    }

    /// <summary>
    /// Compares OPL3Gold timer behavior against standard OPL3 to detect
    /// any mode-specific delay in the rendering pipeline. Both modes use
    /// the same OPL timer mechanism, so iteration counts should be similar.
    /// </summary>
    [Fact]
    public void AdlibGold_TimerBehavior_MatchesStandardOpl3() {
        // Run the standard OPL write delay test in OPL3 mode for baseline
        string comPath = Path.Combine("Resources", "Sound", "opl_write_delay.com");
        byte[] baselineProgram = File.ReadAllBytes(comPath);

        SoundTestHandler opl3Handler = RunSoundTest(baselineProgram,
            enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3);

        opl3Handler.Results.Should().Contain(0x00, "OPL3 baseline Timer 1 should fire");
        int opl3Iterations = opl3Handler.Details[0] | (opl3Handler.Details[1] << 8);

        // Run the same test in OPL3Gold mode
        SoundTestHandler goldHandler = RunSoundTest(baselineProgram,
            enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3Gold);

        goldHandler.Results.Should().Contain(0x00, "OPL3Gold Timer 1 should fire");
        int goldIterations = goldHandler.Details[0] | (goldHandler.Details[1] << 8);

        // Both modes should have the same timer behavior
        goldIterations.Should().Be(opl3Iterations,
            "OPL3Gold should have the same timer iteration count as OPL3 — " +
            "AdLib Gold processing must not introduce additional timer delay");
    }

    private SoundTestHandler RunSoundTest(byte[] program, bool enablePit,
        long maxCycles, SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        [CallerMemberName] string unitTestName = "test") {
        string filePath = Path.GetFullPath($"{unitTestName}_{oplMode}.com");
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
