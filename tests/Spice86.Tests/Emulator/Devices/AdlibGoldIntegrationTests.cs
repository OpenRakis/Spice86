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

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;

using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Integration tests for AdLib Gold initial audio delay. These verify that
/// OPL3Gold mode produces audio immediately upon key-on without excessive
/// startup latency. Audio output is captured at the SDL/player level and
/// analyzed for leading silence that reveals the initial delay bug.
/// </summary>
[Trait("Category", "Sound")]
public class AdlibGoldIntegrationTests {
    private readonly ITestOutputHelper _testOutputHelper;

    public AdlibGoldIntegrationTests(ITestOutputHelper testOutputHelper) {
        _testOutputHelper = testOutputHelper;
    }

    private const int ResultPort = 0x999;
    private const int DetailsPort = 0x998;
    private const int MixerSampleRate = 48000;

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
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
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

        // Save WAV file for manual inspection of the captured audio
        string wavPath = Path.GetFullPath("adlib_gold_capture.wav");
        capturingPlayer.SaveToWav(wavPath);

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
            $"but all {totalFrames} frames ({(double)totalFrames / MixerSampleRate * 1000:F1}ms) were silent " +
            $"(max abs sample value = {maxAbsValue:E3}). " +
            $"WAV saved to: {wavPath}. " +
            "This indicates the AdLib Gold rendering pipeline is not producing audio");
    }

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
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
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

        // Save WAV file for manual inspection of the captured audio
        string wavPath = Path.GetFullPath("opl3_capture.wav");
        capturingPlayer.SaveToWav(wavPath);

        totalFrames.Should().BeGreaterThan(0,
            "the mixer should have produced audio frames during execution");

        // Count non-silent frames in the captured output
        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        // Assert - OPL3 must produce non-silent audio (no delay issue)
        nonSilentCount.Should().BeGreaterThan(0,
            $"captured audio should contain non-silent frames after OPL key-on, " +
            $"but all {totalFrames} frames ({(double)totalFrames / MixerSampleRate * 1000:F1}ms) were silent. " +
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
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
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

    private static void AdvanceCycles(State state, int count) {
        for (int i = 0; i < count; i++) {
            state.IncCycles();
        }
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
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
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
    /// a cycle-based clock, programs it identically to the ASM test,
    /// and checks if the AudioCallback produces non-zero output.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_OPL3GoldProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        CyclesClock clock = new(state, 3000000);
        EmulationLoopScheduler scheduler = new(clock, loggerService);

        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);

        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        // Create OPL in OPL3Gold mode
        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, clock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // Advance time slightly (simulating time passing before first write)
        AdvanceCycles(state, 3000); // 1ms

        // Program OPL registers (same as opl3_note_playback.asm)
        // Reset timers
        opl.WriteByte(0x388, 0x04); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x60); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x04); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x80); AdvanceCycles(state, 30);

        // Operator 0: multiplier=1
        opl.WriteByte(0x388, 0x20); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x01); AdvanceCycles(state, 30);

        // Operator 0: total level = max volume
        opl.WriteByte(0x388, 0x40); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);

        // Operator 0: attack=15 (fastest), decay=0
        opl.WriteByte(0x388, 0x60); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0xF0); AdvanceCycles(state, 30);

        // Operator 0: sustain=0 (max level), release=0
        opl.WriteByte(0x388, 0x80); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);

        // Operator 3 (carrier): same settings
        opl.WriteByte(0x388, 0x23); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x01); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x43); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x63); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0xF0); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x83); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);

        // Channel 0: stereo output, feedback=1, additive
        opl.WriteByte(0x388, 0xC0); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x31); AdvanceCycles(state, 30);

        // Frequency: A4 = 440 Hz
        opl.WriteByte(0x388, 0xA0); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0xA5); AdvanceCycles(state, 30);

        // Key-on + frequency high
        opl.WriteByte(0x388, 0xB0); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x31); AdvanceCycles(state, 30);

        // Advance cycles for 200ms to let OPL render audio
        AdvanceCycles(state, 600000);

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
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        CyclesClock clock = new(state, 3000000);
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, clock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        AdvanceCycles(state, 3000); // 1ms

        // === EXACT SEQUENCE FROM adlib_gold_init_delay.asm ===

        // Step 1: Activate AdLib Gold control
        opl.WriteByte(0x38A, 0xFF); AdvanceCycles(state, 30);

        // Read board options (index 0x00)
        opl.WriteByte(0x38A, 0x00); AdvanceCycles(state, 30);
        byte boardOpts = opl.ReadByte(0x38B);
        boardOpts.Should().Be(0x50, "Board options should indicate 16-bit ISA + surround");

        // Step 2: Set FM volumes to max
        opl.WriteByte(0x38A, 0x09); AdvanceCycles(state, 30);
        opl.WriteByte(0x38B, 0x1F); AdvanceCycles(state, 30);
        opl.WriteByte(0x38A, 0x0A); AdvanceCycles(state, 30);
        opl.WriteByte(0x38B, 0x1F); AdvanceCycles(state, 30);

        // Step 3: Reset timers
        opl.WriteByte(0x388, 0x04); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x60); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x04); AdvanceCycles(state, 30);
        opl.WriteByte(0x389, 0x80); AdvanceCycles(state, 30);

        // Step 4: Deactivate gold control, enable OPL3 mode
        opl.WriteByte(0x38A, 0xFE); AdvanceCycles(state, 30);
        opl.WriteByte(0x38A, 0x05); AdvanceCycles(state, 30);
        opl.WriteByte(0x38B, 0x01); AdvanceCycles(state, 30);

        // Step 5: Program operators (same as ASM)
        opl.WriteByte(0x388, 0x20); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x01); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x40); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x60); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0xF0); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x80); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x23); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x01); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x43); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x63); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0xF0); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0x83); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x00); AdvanceCycles(state, 30);

        // Channel 0: stereo output, feedback=1, additive
        opl.WriteByte(0x388, 0xC0); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x31); AdvanceCycles(state, 30);

        // Frequency + Key-on
        opl.WriteByte(0x388, 0xA0); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0xA5); AdvanceCycles(state, 30);
        opl.WriteByte(0x388, 0xB0); AdvanceCycles(state, 30); opl.WriteByte(0x389, 0x31); AdvanceCycles(state, 30);

        // Advance cycles for 200ms to let OPL render audio
        AdvanceCycles(state, 600000);

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
    /// Reproduces the integrated test conditions with cycle-based CyclesClock advancing.
    /// State.Cycles starts high (simulating BIOS/DOS init) and advances between
    /// writes (simulating CPU execution). This isolates whether the cycle-based
    /// timing interaction causes the silence.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_CyclesClock_WithAdvancingCycles_ProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        // Simulate BIOS/DOS init having executed 100k cycles before OPL creation
        for (int i = 0; i < 100000; i++) { state.IncCycles(); };

        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        CyclesClock realClock = new(state, 3000000);
        EmulationLoopScheduler scheduler = new(realClock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, realClock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // Simulate CPU executing more instructions after OPL creation
        for (int i = 0; i < 10000; i++) { state.IncCycles(); };

        // Gold control init (like the ASM test)
        opl.WriteByte(0x38A, 0xFF); // Activate gold control
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38A, 0x09); // Left FM volume index
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38B, 0x1F); // Left FM volume = max
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38A, 0x0A); // Right FM volume index
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38B, 0x1F); // Right FM volume = max
        for (int i = 0; i < 30; i++) { state.IncCycles(); };

        // Deactivate gold control and enable OPL3 mode
        opl.WriteByte(0x38A, 0xFE);
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38A, 0x05); // OPL3 enable high bank addr
        for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x38B, 0x01); // OPL3 enable
        for (int i = 0; i < 30; i++) { state.IncCycles(); };

        // Timer reset
        opl.WriteByte(0x388, 0x04); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x60); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x04); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x80); for (int i = 0; i < 30; i++) { state.IncCycles(); };

        // OPL register programming
        opl.WriteByte(0x388, 0x20); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x01); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x40); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x00); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x60); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0xF0); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x80); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x00); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x23); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x01); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x43); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x00); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x63); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0xF0); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0x83); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x00); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0xC0); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x31); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0xA0); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0xA5); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x388, 0xB0); for (int i = 0; i < 30; i++) { state.IncCycles(); };
        opl.WriteByte(0x389, 0x31); for (int i = 0; i < 30; i++) { state.IncCycles(); }; // Key-on

        // Simulate busy-wait: advance cycles for 200ms equivalent
        for (int i = 0; i < 600000; i++) { state.IncCycles(); }; // 200ms * 3000 cycles/ms

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
            $"OPL3Gold with cycle-based CyclesClock and advancing cycles should produce audio, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            "If this fails, the cycle-based timing interaction causes silence.");
    }

    /// <summary>
    /// Reproduces the real-game timing conditions: OPL is created (mixer thread
    /// starts immediately), then a significant delay passes (simulating game init)
    /// before OPL registers are written. Uses the real EmulatedClock (Stopwatch-based)
    /// exactly like production code. This test reveals if the channel sleep/wake
    /// mechanism and timing interaction causes the initial silence.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_WithStartupDelay_ProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        // Use the REAL EmulatedClock (Stopwatch-based), matching production code
        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);

        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        // Create OPL in OPL3Gold mode — mixer thread starts running immediately
        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, clock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // === KEY: Simulate game initialization delay ===
        // In the real game, ~1-2 seconds pass while the game initializes
        // before it starts writing OPL registers. During this time, the mixer
        // thread renders frames from an uninitialized OPL chip. The channel
        // may go to sleep after 500ms of no signal.
        Thread.Sleep(1000);

        // Now program the OPL registers (like the real game would after init)
        // Gold control: activate and set FM volumes
        opl.WriteByte(0x38A, 0xFF); // Activate gold control
        opl.WriteByte(0x38A, 0x09); // Left FM volume index
        opl.WriteByte(0x38B, 0x1F); // Left FM volume = max
        opl.WriteByte(0x38A, 0x0A); // Right FM volume index
        opl.WriteByte(0x38B, 0x1F); // Right FM volume = max

        // Deactivate gold control
        opl.WriteByte(0x38A, 0xFE);

        // Timer reset
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x60);
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x80);

        // OPL register programming
        opl.WriteByte(0x388, 0x20); opl.WriteByte(0x389, 0x01); // op0 mult=1
        opl.WriteByte(0x388, 0x40); opl.WriteByte(0x389, 0x00); // op0 vol max
        opl.WriteByte(0x388, 0x60); opl.WriteByte(0x389, 0xF0); // op0 fast attack
        opl.WriteByte(0x388, 0x80); opl.WriteByte(0x389, 0x00); // op0 sustain max
        opl.WriteByte(0x388, 0x23); opl.WriteByte(0x389, 0x01); // op3 mult=1
        opl.WriteByte(0x388, 0x43); opl.WriteByte(0x389, 0x00); // op3 vol max
        opl.WriteByte(0x388, 0x63); opl.WriteByte(0x389, 0xF0); // op3 fast attack
        opl.WriteByte(0x388, 0x83); opl.WriteByte(0x389, 0x00); // op3 sustain max

        // Channel 0: stereo output
        opl.WriteByte(0x388, 0xC0); opl.WriteByte(0x389, 0x31);

        // Frequency + Key-on
        opl.WriteByte(0x388, 0xA0); opl.WriteByte(0x389, 0xA5);
        opl.WriteByte(0x388, 0xB0); opl.WriteByte(0x389, 0x31);

        // Let the mixer thread render audio AFTER key-on
        Thread.Sleep(500);

        opl.Dispose();
        mixer.Dispose();

        // Now analyze the captured audio AFTER the key-on moment
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        // Save WAV for manual inspection
        string wavPath = Path.GetFullPath("opl3gold_startup_delay.wav");
        capturingPlayer.SaveToWav(wavPath);

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0, "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"After 1s startup delay then OPL key-on + 500ms playback, " +
            $"captured audio should contain non-silent frames, " +
            $"but all {totalFrames} frames ({(double)totalFrames / MixerSampleRate * 1000:F1}ms) were silent " +
            $"(max abs = {maxAbsValue:E3}). WAV: {wavPath}. " +
            "This reproduces the real-game initial silence condition.");
    }

    /// <summary>
    /// Same test as above but with regular OPL3 mode (no AdLib Gold processing).
    /// If this passes but the OPL3Gold version fails, the issue is specific to
    /// the AdLib Gold signal processing chain after the startup delay.
    /// </summary>
    [Fact]
    public void Opl_DirectInstance_Opl3_WithStartupDelay_ProducesAudio() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        // Regular OPL3 mode (no AdLib Gold processing)
        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, clock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3, sbBase: 0x220);

        // Same 1s startup delay
        Thread.Sleep(1000);

        // Timer reset
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x60);
        opl.WriteByte(0x388, 0x04);
        opl.WriteByte(0x389, 0x80);

        // OPL register programming
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

        Thread.Sleep(500);

        opl.Dispose();
        mixer.Dispose();

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        string wavPath = Path.GetFullPath("opl3_startup_delay.wav");
        capturingPlayer.SaveToWav(wavPath);

        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);

        totalFrames.Should().BeGreaterThan(0, "mixer should have produced frames");
        nonSilentCount.Should().BeGreaterThan(0,
            $"OPL3 with 1s startup delay should produce audio after key-on, " +
            $"but all {totalFrames} frames were silent (max abs = {maxAbsValue:E3}). " +
            $"WAV: {wavPath}");
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

    /// <summary>
    /// Instruments the audio thread with .NET Metrics (System.Diagnostics.Metrics).
    /// Collects AudioCallback, RenderUpToNow, FIFO, and timing measurements using a
    /// MeterListener, then reports a comprehensive diagnostic summary.
    ///
    /// Uses the real EmulatedClock (Stopwatch-based), matching production, with a
    /// 1-second startup delay before OPL programming to simulate real-game init.
    /// </summary>
    [Fact]
    public void Opl_AudioThread_Metrics_WithStartupDelay() {
        // ── Collect metrics via MeterListener ─────────────────────────
        ConcurrentDictionary<string, List<double>> histograms = new();
        ConcurrentDictionary<string, long> counters = new();

        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) => {
            if (instrument.Meter.Name == "Spice86.Opl") {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => {
            counters.AddOrUpdate(instrument.Name, value, (_, old) => old + value);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => {
            histograms.GetOrAdd(instrument.Name, _ => new()).Add(value);
        });
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => {
            histograms.GetOrAdd(instrument.Name, _ => new()).Add(value);
        });
        listener.Start();

        // ── Set up OPL in OPL3Gold mode ───────────────────────────────
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        Spice86.Audio.Mixer.Mixer mixer = new(loggerService, pauseHandler, capturingPlayer);

        State state = new(CpuModel.INTEL_8086);
        AddressReadWriteBreakpoints ioBreakpoints = new();
        IOPortDispatcher ioPortDispatcher = new(ioBreakpoints, state, loggerService, false);

        EmulatedClock clock = new();
        EmulationLoopScheduler scheduler = new(clock, loggerService);
        ICyclesLimiter cyclesLimiter = Substitute.For<ICyclesLimiter>();
        cyclesLimiter.TargetCpuCyclesPerMs.Returns(3000);
        DualPic dualPic = new(ioPortDispatcher, state, loggerService, false);

        Opl opl = new(mixer, state, ioPortDispatcher, false, loggerService,
            scheduler, clock, cyclesLimiter, dualPic,
            mode: OplMode.Opl3Gold, sbBase: 0x220);

        // ── Startup delay (simulates game init) ──────────────────────
        Thread.Sleep(1000);

        // ── Program OPL registers ────────────────────────────────────
        opl.WriteByte(0x38A, 0xFF); // Activate gold control
        opl.WriteByte(0x38A, 0x09); opl.WriteByte(0x38B, 0x1F); // Left FM vol
        opl.WriteByte(0x38A, 0x0A); opl.WriteByte(0x38B, 0x1F); // Right FM vol
        opl.WriteByte(0x38A, 0xFE); // Deactivate gold control

        opl.WriteByte(0x388, 0x04); opl.WriteByte(0x389, 0x60); // Timer reset
        opl.WriteByte(0x388, 0x04); opl.WriteByte(0x389, 0x80);
        opl.WriteByte(0x388, 0x20); opl.WriteByte(0x389, 0x01); // op0 mult=1
        opl.WriteByte(0x388, 0x40); opl.WriteByte(0x389, 0x00); // op0 vol max
        opl.WriteByte(0x388, 0x60); opl.WriteByte(0x389, 0xF0); // op0 fast attack
        opl.WriteByte(0x388, 0x80); opl.WriteByte(0x389, 0x00); // op0 sustain max
        opl.WriteByte(0x388, 0x23); opl.WriteByte(0x389, 0x01); // op3 mult=1
        opl.WriteByte(0x388, 0x43); opl.WriteByte(0x389, 0x00); // op3 vol max
        opl.WriteByte(0x388, 0x63); opl.WriteByte(0x389, 0xF0); // op3 fast attack
        opl.WriteByte(0x388, 0x83); opl.WriteByte(0x389, 0x00); // op3 sustain max
        opl.WriteByte(0x388, 0xC0); opl.WriteByte(0x389, 0x31); // ch0 stereo
        opl.WriteByte(0x388, 0xA0); opl.WriteByte(0x389, 0xA5); // freq low
        opl.WriteByte(0x388, 0xB0); opl.WriteByte(0x389, 0x31); // key-on

        // ── Let mixer render audio ───────────────────────────────────
        Thread.Sleep(500);

        opl.Dispose();
        mixer.Dispose();
        listener.Dispose();

        // ── Report all collected metrics ──────────────────────────────
        long callbackCount = counters.GetValueOrDefault("opl.audio_callback.count", 0);
        long framesRequested = counters.GetValueOrDefault("opl.audio_callback.frames_requested", 0);
        long fifoServed = counters.GetValueOrDefault("opl.audio_callback.fifo_frames_served", 0);
        long directRendered = counters.GetValueOrDefault("opl.audio_callback.direct_render_frames", 0);
        long fifoStarvations = counters.GetValueOrDefault("opl.audio_callback.fifo_starvations", 0);
        long renderCount = counters.GetValueOrDefault("opl.render_up_to_now.count", 0);
        long renderFrames = counters.GetValueOrDefault("opl.render_up_to_now.frames_generated", 0);
        long wakeUps = counters.GetValueOrDefault("opl.render_up_to_now.wake_ups", 0);

        List<double> callbackDurations = histograms.GetValueOrDefault("opl.audio_callback.duration_ms", new());
        List<double> renderGaps = histograms.GetValueOrDefault("opl.render_up_to_now.gap_ms", new());
        List<double> fifoDepths = histograms.GetValueOrDefault("opl.fifo.depth_at_callback", new());
        List<double> lastRenderedAtCb = histograms.GetValueOrDefault("opl.timing.last_rendered_ms_at_callback", new());
        List<double> clockAtCb = histograms.GetValueOrDefault("opl.timing.clock_elapsed_ms_at_callback", new());
        List<double> clockAtRender = histograms.GetValueOrDefault("opl.timing.clock_elapsed_ms_at_render", new());

        // ── Analyze captured audio ───────────────────────────────────
        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;
        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);
        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        string wavPath = Path.GetFullPath("opl_metrics_startup_delay.wav");
        capturingPlayer.SaveToWav(wavPath);

        // ── Build diagnostic report ──────────────────────────────────
        string report = $"""
            ═══════════════════════════════════════════════════════════════
            OPL Audio Thread Metrics Report
            ═══════════════════════════════════════════════════════════════

            ── AudioCallback (mixer thread) ───────────────────────────────
              Invocations:        {callbackCount}
              Total frames req:   {framesRequested}
              FIFO frames served: {fifoServed} ({(framesRequested > 0 ? (double)fifoServed / framesRequested * 100 : 0):F1}%)
              Direct-rendered:    {directRendered} ({(framesRequested > 0 ? (double)directRendered / framesRequested * 100 : 0):F1}%)
              FIFO starvations:   {fifoStarvations} / {callbackCount} callbacks ({(callbackCount > 0 ? (double)fifoStarvations / callbackCount * 100 : 0):F1}%)
              Duration (ms):      min={SafeAggregate(callbackDurations, s => s.Min()):F3}  avg={SafeAggregate(callbackDurations, s => s.Average()):F3}  max={SafeAggregate(callbackDurations, s => s.Max()):F3}

            ── RenderUpToNow (CPU thread) ─────────────────────────────────
              Invocations:        {renderCount}
              FIFO frames gen'd:  {renderFrames}
              Channel wake-ups:   {wakeUps}
              Gap ms (now-last):  min={SafeAggregate(renderGaps, s => s.Min()):F3}  avg={SafeAggregate(renderGaps, s => s.Average()):F3}  max={SafeAggregate(renderGaps, s => s.Max()):F3}

            ── FIFO Queue ─────────────────────────────────────────────────
              Depth at callback:  min={SafeAggregate(fifoDepths, s => s.Min()):F0}  avg={SafeAggregate(fifoDepths, s => s.Average()):F1}  max={SafeAggregate(fifoDepths, s => s.Max()):F0}

            ── Timing ─────────────────────────────────────────────────────
              _lastRenderedMs@CB: min={SafeAggregate(lastRenderedAtCb, s => s.Min()):F1}  max={SafeAggregate(lastRenderedAtCb, s => s.Max()):F1}
              _clock@CB:          min={SafeAggregate(clockAtCb, s => s.Min()):F1}  max={SafeAggregate(clockAtCb, s => s.Max()):F1}
              _clock@Render:      min={SafeAggregate(clockAtRender, s => s.Min()):F1}  max={SafeAggregate(clockAtRender, s => s.Max()):F1}

            ── Captured Audio ─────────────────────────────────────────────
              Total frames:       {totalFrames} ({(double)totalFrames / MixerSampleRate * 1000:F1}ms)
              Non-silent frames:  {nonSilentCount}
              Max abs value:      {maxAbsValue:E3}
              WAV saved to:       {wavPath}
            ═══════════════════════════════════════════════════════════════
            """;

        // Always write metrics report to xUnit output for diagnostic visibility
        _testOutputHelper.WriteLine(report);

        callbackCount.Should().BeGreaterThan(0,
            $"AudioCallback should have been invoked. Metrics report:\n{report}");

        totalFrames.Should().BeGreaterThan(0,
            $"Mixer should have produced frames. Metrics report:\n{report}");

        nonSilentCount.Should().BeGreaterThan(0,
            $"Captured audio should be non-silent after key-on. Metrics report:\n{report}");
    }

    /// <summary>
    /// Same metrics test but using the integrated emulator with the AdLib Gold
    /// ASM test program. Collects AudioCallback/RenderUpToNow metrics during
    /// the full emulation run to reveal the FIFO starvation pattern.
    /// </summary>
    [Fact]
    public void Opl_AudioThread_Metrics_IntegratedAsmTest() {
        ConcurrentDictionary<string, List<double>> histograms = new();
        ConcurrentDictionary<string, long> counters = new();

        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) => {
            if (instrument.Meter.Name == "Spice86.Opl") {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => {
            counters.AddOrUpdate(instrument.Name, value, (_, old) => old + value);
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => {
            histograms.GetOrAdd(instrument.Name, _ => new()).Add(value);
        });
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => {
            histograms.GetOrAdd(instrument.Name, _ => new()).Add(value);
        });
        listener.Start();

        string comPath = Path.Combine("Resources", "Sound", "adlib_gold_init_delay.com");
        byte[] program = File.ReadAllBytes(comPath);

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        SoundTestHandler testHandler = RunSoundTest(program,
            enablePit: true, maxCycles: 2000000L,
            oplMode: OplMode.Opl3Gold,
            audioPlayer: capturingPlayer);

        listener.Dispose();

        long callbackCount = counters.GetValueOrDefault("opl.audio_callback.count", 0);
        long framesRequested = counters.GetValueOrDefault("opl.audio_callback.frames_requested", 0);
        long fifoServed = counters.GetValueOrDefault("opl.audio_callback.fifo_frames_served", 0);
        long directRendered = counters.GetValueOrDefault("opl.audio_callback.direct_render_frames", 0);
        long fifoStarvations = counters.GetValueOrDefault("opl.audio_callback.fifo_starvations", 0);
        long renderCount = counters.GetValueOrDefault("opl.render_up_to_now.count", 0);
        long renderFrames = counters.GetValueOrDefault("opl.render_up_to_now.frames_generated", 0);
        long wakeUps = counters.GetValueOrDefault("opl.render_up_to_now.wake_ups", 0);

        List<double> callbackDurations = histograms.GetValueOrDefault("opl.audio_callback.duration_ms", new());
        List<double> renderGaps = histograms.GetValueOrDefault("opl.render_up_to_now.gap_ms", new());
        List<double> fifoDepths = histograms.GetValueOrDefault("opl.fifo.depth_at_callback", new());

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;
        int nonSilentCount = CountNonSilentFrames(samples, channels: 2);
        float maxAbsValue = 0;
        for (int i = 0; i < samples.Length; i++) {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbsValue) { maxAbsValue = abs; }
        }

        string wavPath = Path.GetFullPath("opl_metrics_integrated.wav");
        capturingPlayer.SaveToWav(wavPath);

        string report = $"""
            ═══════════════════════════════════════════════════════════════
            OPL Audio Thread Metrics — Integrated ASM Test
            ═══════════════════════════════════════════════════════════════

            ── AudioCallback (mixer thread) ───────────────────────────────
              Invocations:        {callbackCount}
              Total frames req:   {framesRequested}
              FIFO frames served: {fifoServed} ({(framesRequested > 0 ? (double)fifoServed / framesRequested * 100 : 0):F1}%)
              Direct-rendered:    {directRendered} ({(framesRequested > 0 ? (double)directRendered / framesRequested * 100 : 0):F1}%)
              FIFO starvations:   {fifoStarvations} / {callbackCount} callbacks ({(callbackCount > 0 ? (double)fifoStarvations / callbackCount * 100 : 0):F1}%)
              Duration (ms):      min={SafeAggregate(callbackDurations, s => s.Min()):F3}  avg={SafeAggregate(callbackDurations, s => s.Average()):F3}  max={SafeAggregate(callbackDurations, s => s.Max()):F3}

            ── RenderUpToNow (CPU thread) ─────────────────────────────────
              Invocations:        {renderCount}
              FIFO frames gen'd:  {renderFrames}
              Channel wake-ups:   {wakeUps}
              Gap ms (now-last):  min={SafeAggregate(renderGaps, s => s.Min()):F3}  avg={SafeAggregate(renderGaps, s => s.Average()):F3}  max={SafeAggregate(renderGaps, s => s.Max()):F3}

            ── FIFO Queue ─────────────────────────────────────────────────
              Depth at callback:  min={SafeAggregate(fifoDepths, s => s.Min()):F0}  avg={SafeAggregate(fifoDepths, s => s.Average()):F1}  max={SafeAggregate(fifoDepths, s => s.Max()):F0}

            ── ASM Test Results ───────────────────────────────────────────
              Timer 1 fired:      {testHandler.Results.Contains((byte)0x00)}

            ── Captured Audio ─────────────────────────────────────────────
              Total frames:       {totalFrames} ({(double)totalFrames / MixerSampleRate * 1000:F1}ms)
              Non-silent frames:  {nonSilentCount}
              Max abs value:      {maxAbsValue:E3}
              WAV saved to:       {wavPath}
            ═══════════════════════════════════════════════════════════════
            """;

        _testOutputHelper.WriteLine(report);

        callbackCount.Should().BeGreaterThan(0,
            $"AudioCallback should have been invoked. Metrics report:\n{report}");
        totalFrames.Should().BeGreaterThan(0,
            $"Mixer should have produced frames. Metrics report:\n{report}");
        nonSilentCount.Should().BeGreaterThan(0,
            $"Captured audio should be non-silent. Metrics report:\n{report}");
    }

    /// <summary>
    /// Helper to safely compute aggregate statistics on a list.
    /// Returns 0.0 for empty lists.
    /// </summary>
    private static double SafeAggregate(List<double> values, Func<IEnumerable<double>, double> fn) {
        if (values.Count == 0) { return 0.0; }
        return fn(values);
    }

    /// <summary>
    /// Runs the real Dune CD game (DNCDPRG.EXE with ADG330 SBP2227) in AdLib Gold
    /// mode and captures audio output. Verifies that non-silent audio appears within
    /// the first 6 seconds of execution, reproducing the real-world initial silence bug.
    /// Requires DUNE_CD_PATH environment variable pointing to the directory containing
    /// DNCDPRG.EXE. Skips when the game files are not available.
    /// </summary>
    [Fact]
    public void DuneCd_AdlibGold_HasNoExcessiveLeadingSilence() {
        string? duneCdPath = Environment.GetEnvironmentVariable("DUNE_CD_PATH");
        if (string.IsNullOrEmpty(duneCdPath)) {
            // Skip: game files not available
            return;
        }

        string exePath = Path.Combine(duneCdPath, "DNCDPRG.EXE");
        if (!File.Exists(exePath)) {
            // Skip: DNCDPRG.EXE not found at specified path
            return;
        }

        CapturingAudioPlayer capturingPlayer = new(
            new AudioFormat(SampleRate: MixerSampleRate, Channels: 2,
                SampleFormat: SampleFormat.IeeeFloat32));

        // Run for ~10 seconds worth of cycles (RealModeCpuCyclesPerMs * 10000ms)
        // This should be enough to cover the ~6-second silence window
        long maxCycles = ICyclesLimiter.RealModeCpuCyclesPerMs * 10_000L;

        Spice86DependencyInjection spice86DependencyInjection = new Spice86Creator(
            binName: exePath,
            enablePit: true,
            maxCycles: maxCycles,
            installInterruptVectors: true,
            enableA20Gate: true,
            enableXms: true,
            enableEms: true,
            cDrive: duneCdPath,
            sbType: SbType.SBPro2,
            oplMode: OplMode.Opl3Gold,
            audioPlayer: capturingPlayer,
            exeArgs: "ADG330 SBP2227"
        ).Create();

        spice86DependencyInjection.ProgramExecutor.Run();

        // Save WAV for manual inspection
        string wavPath = Path.GetFullPath("dune_cd_adlib_gold.wav");
        capturingPlayer.SaveToWav(wavPath);

        float[] samples = capturingPlayer.GetCapturedSamples();
        int totalFrames = capturingPlayer.CapturedFrameCount;

        // Check first 6 seconds (288000 frames at 48kHz) for non-silent audio
        int sixSecondsFrames = Math.Min(6 * MixerSampleRate, totalFrames);
        int channels = 2;
        int nonSilentInFirst6Seconds = 0;
        int firstNonSilentFrame = -1;

        for (int frame = 0; frame < sixSecondsFrames; frame++) {
            int idx = frame * channels;
            if (idx + 1 >= samples.Length) { break; }
            float left = Math.Abs(samples[idx]);
            float right = Math.Abs(samples[idx + 1]);
            if (left > SilenceThreshold || right > SilenceThreshold) {
                nonSilentInFirst6Seconds++;
                if (firstNonSilentFrame < 0) {
                    firstNonSilentFrame = frame;
                }
            }
        }

        int totalNonSilent = CountNonSilentFrames(samples, channels);

        totalFrames.Should().BeGreaterThan(0,
            "Dune CD should have produced audio frames");

        // The test FAILS if the first 6 seconds are all silent (reproducing the bug)
        nonSilentInFirst6Seconds.Should().BeGreaterThan(0,
            $"Dune CD AdLib Gold music should be audible within the first 6 seconds, " +
            $"but all {sixSecondsFrames} frames ({sixSecondsFrames / 48.0:F1}ms) in that window were silent. " +
            $"Total frames: {totalFrames}, total non-silent: {totalNonSilent}. " +
            $"WAV saved to: {wavPath}");
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
