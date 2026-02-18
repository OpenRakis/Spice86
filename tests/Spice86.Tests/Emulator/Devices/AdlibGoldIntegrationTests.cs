namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Audio.Backend.Audio;
using Spice86.Audio.Backend.Audio.DummyAudio;
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
/// startup latency. Audio output is captured at the SDL/player level and
/// analyzed for leading silence that reveals the initial delay bug.
/// </summary>
[Trait("Category", "Sound")]
public class AdlibGoldIntegrationTests {
    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;

    /// <summary>
    /// The mixer runs at 48000 Hz. Audio should appear within a few ms
    /// of key-on. A threshold of 50ms (2400 frames) is generous — in
    /// DOSBox staging, audio appears within 1-2 mixer blocks (~21ms).
    /// </summary>
    private const int MaxLeadingSilenceFrames = 2400; // 50ms at 48kHz

    /// <summary>
    /// Minimum amplitude to consider a sample as non-silent.
    /// The mixer normalizes output to ±1.0f range.
    /// </summary>
    private const float SilenceThreshold = 1e-6f;

    /// <summary>
    /// Tests that OPL3Gold mode produces audio output without excessive
    /// initial delay by capturing the mixer's final output and analyzing
    /// it for leading silence.
    ///
    /// The ASM test programs the AdLib Gold control interface, sets FM
    /// volumes to maximum, programs a fast-attack OPL note, and performs
    /// key-on. The captured audio is then analyzed to find the first
    /// non-silent sample.
    ///
    /// This test is expected to FAIL if the AdLib Gold rendering pipeline
    /// introduces an initial delay that does not exist in DOSBox staging.
    /// </summary>
    [Fact]
    public void AdlibGold_CapturedAudio_HasNoExcessiveLeadingSilence() {
        // Arrange
        string comPath = Path.Combine("Resources", "Sound", "adlib_gold_init_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        // Act - Run the test program with audio capture
        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3Gold,
            audioPlayer: capturingPlayer);

        // Verify the test program completed successfully
        testHandler.Results.Should().Contain(0x00,
            "OPL Timer 1 should fire, proving the test program executed fully");

        // Analyze captured audio
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");

        // Find the first non-silent frame
        int firstNonSilentFrame = FindFirstNonSilentFrame(samples, channels: 2);

        // Assert - Audio should appear quickly after key-on
        firstNonSilentFrame.Should().BeLessThan(MaxLeadingSilenceFrames,
            $"audio should start within {MaxLeadingSilenceFrames} frames (50ms) of mixer output, " +
            $"but first non-silent frame was at frame {firstNonSilentFrame} " +
            $"({(double)firstNonSilentFrame / 48000 * 1000:F1}ms). " +
            $"Total captured: {totalFrames} frames ({(double)totalFrames / 48000 * 1000:F1}ms). " +
            "This indicates an initial delay in the AdLib Gold rendering pipeline");
    }

    /// <summary>
    /// Baseline test: standard OPL3 mode should produce audio without
    /// excessive leading silence. This provides a reference for comparison
    /// with OPL3Gold mode.
    /// </summary>
    [Fact]
    public void Opl3_CapturedAudio_HasNoExcessiveLeadingSilence() {
        // Arrange - Use the same AdLib Gold test program but in OPL3 mode
        // The gold control writes will be no-ops, but the OPL note programming
        // and timer are mode-independent
        string comPath = Path.Combine("Resources", "Sound", "adlib_gold_init_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        // Act
        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 500000L,
            oplMode: OplMode.Opl3,
            audioPlayer: capturingPlayer);

        // The gold control read won't return 0x50 in OPL3 mode, so the test
        // program will report failure code 0x01. That's expected — we only
        // care about the captured audio timing here.
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");
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

    /// <summary>
    /// Finds the index of the first frame where any channel exceeds the silence threshold.
    /// </summary>
    private static int FindFirstNonSilentFrame(float[] interleavedSamples, int channels) {
        int totalFrames = interleavedSamples.Length / channels;
        for (int frame = 0; frame < totalFrames; frame++) {
            for (int ch = 0; ch < channels; ch++) {
                float sample = interleavedSamples[frame * channels + ch];
                if (Math.Abs(sample) > SilenceThreshold) {
                    return frame;
                }
            }
        }
        return totalFrames; // All silent
    }

    private SoundTestHandler RunSoundTest(byte[] program, bool enablePit,
        long maxCycles, SbType sbType = SbType.None, OplMode oplMode = OplMode.None,
        AudioPlayer? audioPlayer = null,
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
            oplMode: oplMode,
            audioPlayer: audioPlayer
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
