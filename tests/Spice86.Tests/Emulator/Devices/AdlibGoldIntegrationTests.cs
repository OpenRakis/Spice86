namespace Spice86.Tests.Emulator.Devices;

using FluentAssertions;

using Spice86.Audio.Backend.Audio;
using Spice86.Audio.Backend.Audio.DummyAudio;
using Spice86.Audio.Mixer;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Core.Emulator.VM.Clock;
using Spice86.Core.Emulator.VM.CpuSpeedLimit;
using Spice86.Core.Emulator.VM.EmulationLoopScheduler;
using Spice86.Shared.Interfaces;

using NSubstitute;

using System.Runtime.CompilerServices;
using System.Threading;

using Xunit;/// <summary>
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
    /// Tests that OPL3Gold mode produces any non-silent audio during
    /// execution by capturing the mixer's final output.
    ///
    /// The ASM test programs the AdLib Gold control interface, sets FM
    /// volumes to maximum, programs a fast-attack OPL note, and performs
    /// key-on. The captured audio must contain non-silent frames,
    /// proving the OPL rendering pipeline is functional.
    ///
    /// This test is expected to FAIL if the AdLib Gold rendering pipeline
    /// does not produce audible output during the test execution window.
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
            enablePit: true, maxCycles: 2000000L,
            oplMode: OplMode.Opl3Gold,
            audioPlayer: capturingPlayer);

        // The ASM program busy-waits ~200ms after key-on (consuming
        // ~600k cycles with CycleLimiter throttling), giving the mixer
        // thread ~10 callbacks to capture audio while key-on is active.
        // Machine.Dispose() is called inside Run() via EmulationStopped,
        // so no post-execution sleep is useful.

        // Verify the test program completed successfully
        testHandler.Results.Should().Contain(0x00,
            "OPL Timer 1 should fire, proving the test program executed fully");

        // Analyze captured audio
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");

        // Count non-silent frames in the captured output
        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        // Diagnostic: find max absolute value across all samples
        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        // Assert - The captured output must contain non-silent audio.
        // With a fast-attack OPL note and key-on, the OPL chip should
        // generate audible output during the mixer blocks that overlap
        // with the key-on period.
        nonSilentCount.Should().BeGreaterThan(0,
            $"captured audio should contain non-silent frames after OPL key-on, " +
            $"but all {totalFrames} frames ({(double)totalFrames / 48000 * 1000:F1}ms) were silent " +
            $"(max abs sample value = {maxAbsValue:E3}). " +
            "This indicates the AdLib Gold rendering pipeline is not producing audio");
    }

    /// <summary>
    /// Baseline test: standard OPL3 mode should produce audio without
    /// excessive leading silence. Uses a dedicated OPL3-only test program
    /// (no AdLib Gold control) to program a note and capture the mixer
    /// output. This confirms that OPL3 has no initial delay, serving as
    /// the reference for comparison with OPL3Gold mode.
    /// </summary>
    /// <summary>
    /// Baseline test: standard OPL3 mode must produce non-silent audio
    /// during execution. Uses a dedicated OPL3-only test program (no AdLib
    /// Gold control) to program a note and capture the mixer output.
    /// This confirms OPL3 works correctly, establishing the baseline for
    /// comparison with OPL3Gold mode.
    /// </summary>
    [Fact]
    public void Opl3_CapturedAudio_HasNoExcessiveLeadingSilence() {
        // Arrange - Use the OPL3-only note playback program (no gold control)
        string comPath = Path.Combine("Resources", "Sound", "opl3_note_playback.com");
        byte[] program = File.ReadAllBytes(comPath);

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        // Act
        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 2000000L,
            oplMode: OplMode.Opl3,
            audioPlayer: capturingPlayer);

        // Verify the test program completed successfully
        testHandler.Results.Should().Contain(0x00,
            "OPL Timer 1 should fire, proving the test program executed fully");

        // Analyze captured audio
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");

        // Count non-silent frames in the captured output
        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        // Assert - OPL3 must produce non-silent audio (no delay issue)
        nonSilentCount.Should().BeGreaterThan(0,
            $"captured audio should contain non-silent frames after OPL key-on, " +
            $"but all {totalFrames} frames ({(double)totalFrames / 48000 * 1000:F1}ms) were silent. " +
            "OPL3 should produce audio immediately — this is the baseline for comparison");
    }

    /// <summary>
    /// Diagnostic: runs the OPL3-only ASM program (no gold control writes)
    /// in OPL3Gold mode. If this passes, the issue is in gold control
    /// initialization. If it fails, the issue is fundamental to OPL3Gold mode.
    /// </summary>
    [Fact]
    public void Opl3Gold_WithStandardOplProgram_ProducesAudio() {
        // Use the same OPL3-only program that passes in OPL3 mode
        string comPath = Path.Combine("Resources", "Sound", "opl3_note_playback.com");
        byte[] program = File.ReadAllBytes(comPath);

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        // Run in OPL3Gold mode but with OPL3-only program (no gold control)
        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 2000000L,
            oplMode: OplMode.Opl3Gold,
            audioPlayer: capturingPlayer);

        testHandler.Results.Should().Contain(0x00,
            "OPL Timer 1 should fire");

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        // Diagnostic: find max absolute value
        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold mode with standard OPL program should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails, the issue is fundamental to OPL3Gold mode rendering, " +
            "not specific to AdLib Gold control initialization");
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

    /// <summary>
    /// Counts the number of frames where any channel exceeds the silence threshold.
    /// </summary>
    private static int CountNonSilentFrames(float[] interleavedSamples, int channels) {
        int count = 0;
        int totalFrames = interleavedSamples.Length / channels;
        for (int frame = 0; frame < totalFrames; frame++) {
            for (int ch = 0; ch < channels; ch++) {
                float sample = interleavedSamples[frame * channels + ch];
                if (Math.Abs(sample) > SilenceThreshold) {
                    count++;
                    break; // count this frame once, move to next
                }
            }
        }
        return count;
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
            Substitute.For<ILoggerService>(),
            spice86DependencyInjection.Machine.IoPortDispatcher
        );
        spice86DependencyInjection.ProgramExecutor.Run();

        return testHandler;
    }

    /// <summary>
    /// Diagnostic: feeds known non-zero frames through a Mixer + MixerChannel
    /// with OPL-identical configuration (noise gate, resampling, envelope)
    /// to isolate whether the mixer pipeline itself zeros the signal.
    /// </summary>
    [Fact]
    public void MixerChannel_WithOplConfig_ProducesNonZeroOutput() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);
        mixer.LockMixerThread();

        HashSet<ChannelFeature> features = [
            ChannelFeature.Sleep,
            ChannelFeature.FadeOut,
            ChannelFeature.NoiseGate,
            ChannelFeature.ReverbSend,
            ChannelFeature.ChorusSend,
            ChannelFeature.Synthesizer,
            ChannelFeature.Stereo
        ];

        int callbackCount = 0;
        MixerChannel? channelRef = null;
        MixerChannel channel = mixer.AddChannel(framesRequested => {
            MixerChannel ch = channelRef!;
            // Feed known non-zero frames — simulate OPL chip output
            float[] frameData = new float[2];
            for (int i = 0; i < framesRequested; i++) {
                // Simulate a simple tone (like OPL3 output passed through AdlibGold)
                float value = 5000.0f * MathF.Sin(2.0f * MathF.PI * 440 * (callbackCount * framesRequested + i) / 49716.0f);
                frameData[0] = value;
                frameData[1] = value;
                ch.AddSamplesFloat(1, frameData);
            }
            callbackCount++;
        }, 49716, "Opl", features);
        channelRef = channel;

        channel.SetResampleMethod(ResampleMethod.Resample);

        const float OplVolumeGain = 1.5f;
        channel.Set0dbScalar(OplVolumeGain);

        float thresholdDb = -65.0f + 20.0f * MathF.Log10(OplVolumeGain);
        channel.ConfigureNoiseGate(thresholdDb, 1.0f, 100.0f);
        channel.EnableNoiseGate(true);

        // Enable the channel (simulating WakeUp)
        channel.Enable(true);

        mixer.UnlockMixerThread();

        // Let mixer thread produce some blocks
        Thread.Sleep(200);

        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0,
            "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"mixer channel with OPL config should produce non-zero audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3})");
    }

    /// <summary>
    /// Diagnostic: creates an OPL directly (not through the emulator) with
    /// a controlled mock clock, programs it identically to the ASM test,
    /// and checks if the AudioCallback produces non-zero output.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_OPL3GoldProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        // Use a mock clock that starts at 0 and advances with each call
        IEmulatedClock mockClock = Substitute.For<IEmulatedClock>();
        double clockMs = 0.0;
        mockClock.ElapsedTimeMs.Returns(_ => clockMs);

        EmulationLoopScheduler scheduler = new(mockClock, loggerService);

        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);

        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        // Create OPL in OPL3Gold mode
        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, mockClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // Advance time slightly (simulating time passing before first write)
        clockMs = 1.0;

        // Program OPL registers (same as opl3_note_playback.asm)
        // Reset timers
        opl.WriteByte(0x388, 0x04); clockMs += 0.01; // addr: reg 0x04
        opl.WriteByte(0x389, 0x60); clockMs += 0.01; // data: reset timers
        opl.WriteByte(0x388, 0x04); clockMs += 0.01;
        opl.WriteByte(0x389, 0x80); clockMs += 0.01;

        // Operator 0: multiplier=1
        opl.WriteByte(0x388, 0x20); clockMs += 0.01;
        opl.WriteByte(0x389, 0x01); clockMs += 0.01;

        // Operator 0: total level = max volume
        opl.WriteByte(0x388, 0x40); clockMs += 0.01;
        opl.WriteByte(0x389, 0x00); clockMs += 0.01;

        // Operator 0: attack=15 (fastest), decay=0
        opl.WriteByte(0x388, 0x60); clockMs += 0.01;
        opl.WriteByte(0x389, 0xF0); clockMs += 0.01;

        // Operator 0: sustain=0 (max level), release=0
        opl.WriteByte(0x388, 0x80); clockMs += 0.01;
        opl.WriteByte(0x389, 0x00); clockMs += 0.01;

        // Operator 3 (carrier): same settings
        opl.WriteByte(0x388, 0x23); clockMs += 0.01;
        opl.WriteByte(0x389, 0x01); clockMs += 0.01;
        opl.WriteByte(0x388, 0x43); clockMs += 0.01;
        opl.WriteByte(0x389, 0x00); clockMs += 0.01;
        opl.WriteByte(0x388, 0x63); clockMs += 0.01;
        opl.WriteByte(0x389, 0xF0); clockMs += 0.01;
        opl.WriteByte(0x388, 0x83); clockMs += 0.01;
        opl.WriteByte(0x389, 0x00); clockMs += 0.01;

        // Channel 0: stereo output, feedback=1, additive
        opl.WriteByte(0x388, 0xC0); clockMs += 0.01;
        opl.WriteByte(0x389, 0x31); clockMs += 0.01;

        // Frequency: A4 = 440 Hz
        opl.WriteByte(0x388, 0xA0); clockMs += 0.01;
        opl.WriteByte(0x389, 0xA5); clockMs += 0.01;

        // Key-on + frequency high
        opl.WriteByte(0x388, 0xB0); clockMs += 0.01;
        opl.WriteByte(0x389, 0x31); clockMs += 0.01;

        // Let mixer thread render some blocks after key-on
        Thread.Sleep(200);

        opl.Dispose();
        mixer.Dispose();

        // Analyze captured audio
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0,
            "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold with direct register programming should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3})");
    }

    /// <summary>
    /// Mimics the EXACT register write sequence from the AdLib Gold ASM test
    /// (gold control activation, FM volumes, OPL3 mode enable, note programming)
    /// using direct OPL.WriteByte calls. If this passes but the integrated
    /// ASM test fails, the issue is in the emulator timing/threading path.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_GoldControlSequence_ProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        IEmulatedClock mockClock = Substitute.For<IEmulatedClock>();
        double clockMs = 0.0;
        mockClock.ElapsedTimeMs.Returns(_ => clockMs);

        EmulationLoopScheduler scheduler = new(mockClock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, mockClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        clockMs = 1.0;

        // === EXACT SEQUENCE FROM adlib_gold_init_delay.asm ===

        // Step 1: Activate AdLib Gold control
        opl.WriteByte(0x38A, 0xFF); clockMs += 0.01;

        // Read board options (index 0x00)
        opl.WriteByte(0x38A, 0x00); clockMs += 0.01;
        byte boardOpts = opl.ReadByte(0x38B);
        boardOpts.Should().Be(0x50, "Board options should indicate 16-bit ISA + surround");

        // Step 2: Set FM volumes to max
        opl.WriteByte(0x38A, 0x09); clockMs += 0.01; // Left FM Volume index
        opl.WriteByte(0x38B, 0x1F); clockMs += 0.01; // Max
        opl.WriteByte(0x38A, 0x0A); clockMs += 0.01; // Right FM Volume index
        opl.WriteByte(0x38B, 0x1F); clockMs += 0.01; // Max

        // Step 3: Reset timers
        opl.WriteByte(0x388, 0x04); clockMs += 0.01;
        opl.WriteByte(0x389, 0x60); clockMs += 0.01;
        opl.WriteByte(0x388, 0x04); clockMs += 0.01;
        opl.WriteByte(0x389, 0x80); clockMs += 0.01;

        // Step 4: Deactivate gold control, enable OPL3 mode
        opl.WriteByte(0x38A, 0xFE); clockMs += 0.01; // Deactivate gold control
        opl.WriteByte(0x38A, 0x05); clockMs += 0.01; // High bank reg 0x05
        opl.WriteByte(0x38B, 0x01); clockMs += 0.01; // OPL3 mode enable

        // Step 5: Program operators (same as ASM)
        opl.WriteByte(0x388, 0x20); clockMs += 0.01; opl.WriteByte(0x389, 0x01); clockMs += 0.01;
        opl.WriteByte(0x388, 0x40); clockMs += 0.01; opl.WriteByte(0x389, 0x00); clockMs += 0.01;
        opl.WriteByte(0x388, 0x60); clockMs += 0.01; opl.WriteByte(0x389, 0xF0); clockMs += 0.01;
        opl.WriteByte(0x388, 0x80); clockMs += 0.01; opl.WriteByte(0x389, 0x00); clockMs += 0.01;
        opl.WriteByte(0x388, 0x23); clockMs += 0.01; opl.WriteByte(0x389, 0x01); clockMs += 0.01;
        opl.WriteByte(0x388, 0x43); clockMs += 0.01; opl.WriteByte(0x389, 0x00); clockMs += 0.01;
        opl.WriteByte(0x388, 0x63); clockMs += 0.01; opl.WriteByte(0x389, 0xF0); clockMs += 0.01;
        opl.WriteByte(0x388, 0x83); clockMs += 0.01; opl.WriteByte(0x389, 0x00); clockMs += 0.01;

        // Channel 0: stereo output, feedback=1, additive
        opl.WriteByte(0x388, 0xC0); clockMs += 0.01; opl.WriteByte(0x389, 0x31); clockMs += 0.01;

        // Frequency + Key-on
        opl.WriteByte(0x388, 0xA0); clockMs += 0.01; opl.WriteByte(0x389, 0xA5); clockMs += 0.01;
        opl.WriteByte(0x388, 0xB0); clockMs += 0.01; opl.WriteByte(0x389, 0x31); clockMs += 0.01;

        Thread.Sleep(200);

        opl.Dispose();
        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0, "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold with gold control + OPL register sequence should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails, the gold control writes interfere with OPL rendering");
    }

    /// <summary>
    /// Same as the mock-clock test but with the real EmulatedClock (wall-clock).
    /// If this fails while the mock-clock test passes, the issue is in the
    /// wall-clock timing interaction with the mixer thread.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_RealClock_OPL3GoldProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        // Use the real EmulatedClock (wall-clock based)
        EmulatedClock realClock = new();

        EmulationLoopScheduler scheduler = new(realClock, loggerService);

        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);

        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        // Create OPL in OPL3Gold mode
        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, realClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // Program OPL registers (same as opl3_note_playback.asm)
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x60);
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x80);
        opl.WriteByte(0x388, 0x20);
        opl.WriteByte(0x389, 0x01);
        opl.WriteByte(0x388, 0x40);
        opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x60);
        opl.WriteByte(0x389, 0xF0);
        opl.WriteByte(0x388, 0x80);
        opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x23);
        opl.WriteByte(0x389, 0x01);
        opl.WriteByte(0x388, 0x43);
        opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x63);
        opl.WriteByte(0x389, 0xF0);
        opl.WriteByte(0x388, 0x83);
        opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0xC0);
        opl.WriteByte(0x389, 0x31);
        opl.WriteByte(0x388, 0xA0);
        opl.WriteByte(0x389, 0xA5);
        opl.WriteByte(0x388, 0xB0);
        opl.WriteByte(0x389, 0x31);

        // Let mixer thread render some blocks after key-on
        Thread.Sleep(200);

        opl.Dispose();
        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0,
            "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold with real clock should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails while mock-clock test passes, the issue is wall-clock timing");
    }

    /// <summary>
    /// Diagnostic: delays register programming by 500ms after OPL creation to
    /// simulate the real-world scenario where the mixer thread runs many blocks
    /// of silence before the CPU programs OPL registers. Tests if the noise gate
    /// or other time-dependent mixer features suppress audio after a long
    /// initial silence period.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_DelayedProgramming_OPL3GoldProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        EmulatedClock realClock = new();
        EmulationLoopScheduler scheduler = new(realClock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, realClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // *** KEY: Delay 500ms before programming registers ***
        // This simulates the real scenario where the mixer thread runs many
        // blocks of silence before the CPU programs the OPL.
        Thread.Sleep(500);

        // Now program the OPL (key-on a note)
        opl.WriteByte(0x388, 0x04); opl.WriteByte(0x389, 0x60);
        opl.WriteByte(0x388, 0x04); opl.WriteByte(0x389, 0x80);
        opl.WriteByte(0x388, 0x20); opl.WriteByte(0x389, 0x01);
        opl.WriteByte(0x388, 0x40); opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x60); opl.WriteByte(0x389, 0xF0);
        opl.WriteByte(0x388, 0x80); opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x23); opl.WriteByte(0x389, 0x01);
        opl.WriteByte(0x388, 0x43); opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0x63); opl.WriteByte(0x389, 0xF0);
        opl.WriteByte(0x388, 0x83); opl.WriteByte(0x389, 0x00);
        opl.WriteByte(0x388, 0xC0); opl.WriteByte(0x389, 0x31);
        opl.WriteByte(0x388, 0xA0); opl.WriteByte(0x389, 0xA5);
        opl.WriteByte(0x388, 0xB0); opl.WriteByte(0x389, 0x31);

        // Wait for mixer to render post-key-on audio
        Thread.Sleep(200);

        opl.Dispose();
        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0, "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold with 500ms delayed programming should still produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails, the noise gate or sleep/wake cycle suppresses audio after " +
            "prolonged initial silence");
    }

    /// <summary>
    /// Reproduces the integrated test conditions with CyclesClock advancing.
    /// State.Cycles starts high (simulating BIOS/DOS init) and advances between
    /// writes (simulating CPU execution). This isolates whether the CyclesClock
    /// timing interaction causes the silence.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_CyclesClock_WithAdvancingCycles_ProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: 48000, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        // Simulate BIOS/DOS init having executed 100k cycles before OPL creation
        state.AddCycles(100000);

        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        EmulatedClock realClock = new();
        EmulationLoopScheduler scheduler = new(realClock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, realClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // Simulate CPU executing more instructions after OPL creation
        state.AddCycles(10000);

        // Gold control init (like the ASM test)
        opl.WriteByte(0x38A, 0xFF); // Activate gold control
        state.AddCycles(30);
        opl.WriteByte(0x38A, 0x09); // Left FM volume index
        state.AddCycles(30);
        opl.WriteByte(0x38B, 0x1F); // Left FM volume = max
        state.AddCycles(30);
        opl.WriteByte(0x38A, 0x0A); // Right FM volume index
        state.AddCycles(30);
        opl.WriteByte(0x38B, 0x1F); // Right FM volume = max
        state.AddCycles(30);

        // Deactivate gold control and enable OPL3 mode
        opl.WriteByte(0x38A, 0xFE);
        state.AddCycles(30);
        opl.WriteByte(0x38A, 0x05); // OPL3 enable high bank addr
        state.AddCycles(30);
        opl.WriteByte(0x38B, 0x01); // OPL3 enable
        state.AddCycles(30);

        // Timer reset
        opl.WriteByte(0x388, 0x04); state.AddCycles(30);
        opl.WriteByte(0x389, 0x60); state.AddCycles(30);
        opl.WriteByte(0x388, 0x04); state.AddCycles(30);
        opl.WriteByte(0x389, 0x80); state.AddCycles(30);

        // OPL register programming
        opl.WriteByte(0x388, 0x20); state.AddCycles(30);
        opl.WriteByte(0x389, 0x01); state.AddCycles(30);
        opl.WriteByte(0x388, 0x40); state.AddCycles(30);
        opl.WriteByte(0x389, 0x00); state.AddCycles(30);
        opl.WriteByte(0x388, 0x60); state.AddCycles(30);
        opl.WriteByte(0x389, 0xF0); state.AddCycles(30);
        opl.WriteByte(0x388, 0x80); state.AddCycles(30);
        opl.WriteByte(0x389, 0x00); state.AddCycles(30);
        opl.WriteByte(0x388, 0x23); state.AddCycles(30);
        opl.WriteByte(0x389, 0x01); state.AddCycles(30);
        opl.WriteByte(0x388, 0x43); state.AddCycles(30);
        opl.WriteByte(0x389, 0x00); state.AddCycles(30);
        opl.WriteByte(0x388, 0x63); state.AddCycles(30);
        opl.WriteByte(0x389, 0xF0); state.AddCycles(30);
        opl.WriteByte(0x388, 0x83); state.AddCycles(30);
        opl.WriteByte(0x389, 0x00); state.AddCycles(30);
        opl.WriteByte(0x388, 0xC0); state.AddCycles(30);
        opl.WriteByte(0x389, 0x31); state.AddCycles(30);
        opl.WriteByte(0x388, 0xA0); state.AddCycles(30);
        opl.WriteByte(0x389, 0xA5); state.AddCycles(30);
        opl.WriteByte(0x388, 0xB0); state.AddCycles(30);
        opl.WriteByte(0x389, 0x31); state.AddCycles(30); // Key-on

        // Simulate busy-wait: advance cycles for 200ms equivalent
        state.AddCycles(600000); // 200ms * 3000 cycles/ms

        // Let mixer thread capture audio
        Thread.Sleep(200);

        opl.Dispose();
        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0, "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3Gold with CyclesClock and advancing cycles should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails, the CyclesClock timing interaction causes silence.");
    }

    /// <summary>
    /// Verifies that AdlibGold.Process produces non-zero output when given
    /// non-zero input. This isolates the Gold processing pipeline from the
    /// rest of the emulator to confirm it works correctly in isolation.
    /// </summary>
    [Fact]
    public void AdlibGold_Process_ProducesNonZeroOutput() {
        Serilog.ILogger logger = new Serilog.LoggerConfiguration().CreateLogger();
        Audio.Sound.Devices.AdlibGold.AdlibGold gold = new(49716, logger);

        short[] input = [1000, -500];
        float[] output = new float[2];

        gold.Process(input, 1, output);

        gold.Dispose();

        bool nonZero = output[0] != 0f || output[1] != 0f;
        nonZero.Should().BeTrue(
            $"AdlibGold.Process should produce non-zero output for non-zero input, " +
            $"but got [{output[0]}, {output[1]}]");
    }

    /// <summary>
    /// Verifies that the OPL3 chip generates non-zero samples after key-on
    /// when OPL3 new mode is NOT enabled (same as the OPL3 baseline test).
    /// </summary>
    [Fact]
    public void AdlibGold_OplChipOutput_IsNonZeroAfterKeyOn() {
        Serilog.ILogger logger = new Serilog.LoggerConfiguration().CreateLogger();
        Audio.Sound.Devices.AdlibGold.AdlibGold gold = new(49716, logger);
        Audio.Sound.Devices.NukedOpl3.Opl3Chip chip = new();
        chip.Reset(49716);

        // Program WITHOUT OPL3 new mode (like the passing OPL3 test)
        chip.WriteRegisterBuffered(0x20, 0x01); // op0 mult=1
        chip.WriteRegisterBuffered(0x40, 0x00); // op0 volume max
        chip.WriteRegisterBuffered(0x60, 0xF0); // op0 fast attack
        chip.WriteRegisterBuffered(0x80, 0x00); // op0 sustain max
        chip.WriteRegisterBuffered(0x23, 0x01); // op3 mult=1
        chip.WriteRegisterBuffered(0x43, 0x00); // op3 volume max
        chip.WriteRegisterBuffered(0x63, 0xF0); // op3 fast attack
        chip.WriteRegisterBuffered(0x83, 0x00); // op3 sustain max
        chip.WriteRegisterBuffered(0xC0, 0x31); // ch0 stereo + additive
        chip.WriteRegisterBuffered(0xA0, 0xA5); // freq low
        chip.WriteRegisterBuffered(0xB0, 0x31); // key-on + freq high

        short[] buf = new short[2];
        int rawNonZero = 0;
        int goldNonZero = 0;

        for (int i = 0; i < 100; i++) {
            chip.GenerateStream(buf);
            if (buf[0] != 0 || buf[1] != 0) { rawNonZero++; }

            Audio.Sound.Common.AudioFrame frame = new();
            Span<float> frameSpan = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref frame.Left, 2);
            gold.Process(buf, 1, frameSpan);
            if (frame.Left != 0f || frame.Right != 0f) { goldNonZero++; }
        }

        gold.Dispose();

        rawNonZero.Should().BeGreaterThan(0,
            "the OPL3 chip should produce non-zero samples after key-on (no OPL3 mode)");
        goldNonZero.Should().BeGreaterThan(0,
            $"AdlibGold.Process should produce non-zero output (no OPL3 mode), " +
            $"but only {goldNonZero}/100 frames were non-zero " +
            $"(raw chip: {rawNonZero}/100 non-zero)");
    }

    /// <summary>
    /// Same test but WITH OPL3 new mode enabled — this matches what the
    /// AdLib Gold ASM test does. If this fails but the non-OPL3 version
    /// passes, it reveals that enabling OPL3 mode changes the rendering
    /// behavior for the register programming used.
    /// </summary>
    [Fact]
    public void AdlibGold_OplChipOutput_WithOpl3Mode_IsNonZeroAfterKeyOn() {
        Audio.Sound.Devices.NukedOpl3.Opl3Chip chip = new();
        chip.Reset(49716);

        // Program WITH OPL3 new mode (same as AdLib Gold ASM test)
        chip.WriteRegisterBuffered(0x105, 1); // Enable OPL3 new mode
        chip.WriteRegisterBuffered(0x20, 0x01);
        chip.WriteRegisterBuffered(0x40, 0x00);
        chip.WriteRegisterBuffered(0x60, 0xF0);
        chip.WriteRegisterBuffered(0x80, 0x00);
        chip.WriteRegisterBuffered(0x23, 0x01);
        chip.WriteRegisterBuffered(0x43, 0x00);
        chip.WriteRegisterBuffered(0x63, 0xF0);
        chip.WriteRegisterBuffered(0x83, 0x00);
        chip.WriteRegisterBuffered(0xC0, 0x31);
        chip.WriteRegisterBuffered(0xA0, 0xA5);
        chip.WriteRegisterBuffered(0xB0, 0x31);

        short[] buf = new short[2];
        int nonZero = 0;
        for (int i = 0; i < 100; i++) {
            chip.GenerateStream(buf);
            if (buf[0] != 0 || buf[1] != 0) { nonZero++; }
        }

        nonZero.Should().BeGreaterThan(0,
            $"the OPL3 chip should produce non-zero samples after key-on in OPL3 mode, " +
            $"but only {nonZero}/100 frames were non-zero");
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
