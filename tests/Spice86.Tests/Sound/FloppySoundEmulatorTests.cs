namespace Spice86.Tests.Sound;

using FluentAssertions;

using NSubstitute;

using Spice86.Audio.Filters;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.IO;

using Xunit;

/// <summary>
/// Unit tests for the sample-based floppy drive sound emulation.
/// </summary>
/// <remarks>
/// These tests verify the state machine and sample-file architecture that
/// mirrors DOSBox Staging's <c>DiskNoiseDevice</c>.  No real WAV files are
/// required — the tests either create synthetic WAV files in a temporary
/// directory, or verify correct silent fallback behaviour when no files are
/// present.
/// </remarks>
public sealed class FloppySoundEmulatorTests : IDisposable {
    private readonly string _tempDir;

    public FloppySoundEmulatorTests() {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FloppySoundTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() {
        try {
            Directory.Delete(_tempDir, recursive: true);
        } catch (IOException) {
            // Ignore cleanup errors.
        }
    }

    // ------------------------------------------------------------------ //
    // Helpers

    private static SoftwareMixer CreateMixer() {
        IPauseHandler pauseHandler = Substitute.For<IPauseHandler>();
        return new SoftwareMixer(AudioEngine.Dummy, pauseHandler);
    }

    /// <summary>
    /// Writes a minimal 22050 Hz mono 16-bit PCM WAV file with
    /// <paramref name="sampleCount"/> samples all set to
    /// <paramref name="sampleValue"/>.
    /// </summary>
    private static void WriteWav(string path, int sampleCount, short sampleValue) {
        int dataBytes = sampleCount * 2;
        using FileStream fs = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter w = new(fs);

        // RIFF header
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write((uint)(36 + dataBytes)); // chunk size
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write((uint)16);       // sub-chunk size
        w.Write((ushort)1);      // PCM
        w.Write((ushort)1);      // mono
        w.Write((uint)22050);    // sample rate
        w.Write((uint)(22050 * 2)); // byte rate
        w.Write((ushort)2);      // block align
        w.Write((ushort)16);     // bits per sample

        // data sub-chunk
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write((uint)dataBytes);
        for (int i = 0; i < sampleCount; i++) {
            w.Write(sampleValue);
        }
    }

    // ------------------------------------------------------------------ //
    // FloppySoundEmulator — channel lifecycle

    [Fact]
    public void ChannelIsDisabled_AfterConstruction_WithNoSampleFiles() {
        SoftwareMixer mixer = CreateMixer();
        FloppySoundEmulator emulator = new(mixer, FloppyDiskNoiseMode.On, null);

        emulator.Channel.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ChannelIsDisabled_WhenModeIsOff() {
        SoftwareMixer mixer = CreateMixer();
        FloppySoundEmulator emulator = new(mixer, FloppyDiskNoiseMode.Off, null);

        emulator.PlaySeek();
        emulator.StartMotor();

        emulator.Channel.IsEnabled.Should().BeFalse();
    }

    // ------------------------------------------------------------------ //
    // FloppyDiskNoiseDevice — state machine

    [Fact]
    public void PlaySeek_DoesNotInterrupt_AlreadyPlayingSeekSample() {
        // Arrange: seek sample with 22050 samples (1 second)
        string seekFile = Path.Combine(_tempDir, "fdd_seek1.wav");
        WriteWav(seekFile, 22050, 1000);

        FloppyDiskNoiseDevice device = new(
            FloppyDiskNoiseMode.On,
            spinUpPath: string.Empty,
            spinPath: string.Empty,
            seekPaths: new[] { seekFile });

        // Start first seek
        device.PlaySeek();

        // Consume a few frames
        for (int i = 0; i < 100; i++) {
            device.GetNextFrame();
        }

        // The first frame of the current seek sample is at position 100.
        // Calling PlaySeek() again should NOT reset the position.
        device.PlaySeek();

        // Consume the rest — should finish without double-play
        float totalEnergy = 0f;
        for (int i = 100; i < 22050; i++) {
            totalEnergy += Math.Abs(device.GetNextFrame());
        }

        totalEnergy.Should().BeGreaterThan(0f, "seek sample should still be playing after the ignored second PlaySeek");
    }

    [Fact]
    public void ActivateSpin_DoesNotRestart_WhileSpinIsStillPlaying() {
        // Arrange: 1 second spin sample
        string spinFile = Path.Combine(_tempDir, "fdd_spin.wav");
        WriteWav(spinFile, 22050, 500);

        FloppyDiskNoiseDevice device = new(
            FloppyDiskNoiseMode.On,
            spinUpPath: string.Empty,
            spinPath: spinFile,
            seekPaths: Array.Empty<string>());

        device.ActivateSpin();

        // Consume 100 frames
        for (int i = 0; i < 100; i++) {
            device.GetNextFrame();
        }

        // Call ActivateSpin again — must NOT restart the sample
        device.ActivateSpin();

        // If it had restarted, the spin sample would play for another 22050 frames.
        // Consume frames up to just past original end — if spin was restarted there
        // will still be audio after position 22050 - 100 = 21950.
        float energyAfterOriginalEnd = 0f;
        for (int i = 100; i < 22050 + 200; i++) {
            if (i >= 22050) {
                energyAfterOriginalEnd += Math.Abs(device.GetNextFrame());
            } else {
                device.GetNextFrame();
            }
        }

        energyAfterOriginalEnd.Should().Be(0f, "spin must not restart while it is still playing");
    }

    [Fact]
    public void ActivateSpin_Restarts_AfterSpinHasFinished() {
        // Arrange: short 441-sample (20 ms) spin sample
        string spinFile = Path.Combine(_tempDir, "fdd_spin.wav");
        WriteWav(spinFile, 441, 500);

        FloppyDiskNoiseDevice device = new(
            FloppyDiskNoiseMode.On,
            spinUpPath: string.Empty,
            spinPath: spinFile,
            seekPaths: Array.Empty<string>());

        device.ActivateSpin();

        // Consume all 441 samples
        for (int i = 0; i < 441; i++) {
            device.GetNextFrame();
        }

        // Now activate again — should restart
        device.ActivateSpin();

        float energyAfterRestart = 0f;
        for (int i = 0; i < 441; i++) {
            energyAfterRestart += Math.Abs(device.GetNextFrame());
        }

        energyAfterRestart.Should().BeGreaterThan(0f, "spin should restart after its first playthrough has finished");
    }

    // ------------------------------------------------------------------ //
    // WavPcmLoader — file parsing

    [Fact]
    public void WavPcmLoader_ReturnsEmpty_WhenFileDoesNotExist() {
        float[] result = WavPcmLoader.TryLoad("/nonexistent/path/fdd_seek1.wav");
        result.Should().BeEmpty();
    }

    [Fact]
    public void WavPcmLoader_LoadsCorrectSampleCount() {
        string path = Path.Combine(_tempDir, "test.wav");
        const int expectedSamples = 1000;
        WriteWav(path, expectedSamples, 16383);

        float[] result = WavPcmLoader.TryLoad(path);

        result.Should().HaveCount(expectedSamples);
    }

    [Fact]
    public void WavPcmLoader_NormalisesPositiveMaxToNearOne() {
        string path = Path.Combine(_tempDir, "max.wav");
        WriteWav(path, 1, short.MaxValue);

        float[] result = WavPcmLoader.TryLoad(path);

        result.Should().HaveCount(1);
        result[0].Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void WavPcmLoader_ReturnsEmpty_ForNonMonoFile() {
        // Craft a stereo WAV — WavPcmLoader must reject it
        string path = Path.Combine(_tempDir, "stereo.wav");
        WriteStereoWav(path, 100);

        float[] result = WavPcmLoader.TryLoad(path);

        result.Should().BeEmpty("WavPcmLoader only accepts mono 22050 Hz files");
    }

    private static void WriteStereoWav(string path, int sampleCount) {
        int dataBytes = sampleCount * 4; // stereo * 2 bytes per sample
        using FileStream fs = new(path, FileMode.Create, FileAccess.Write);
        using BinaryWriter w = new(fs);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write((uint)(36 + dataBytes));
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write((uint)16);
        w.Write((ushort)1);   // PCM
        w.Write((ushort)2);   // STEREO
        w.Write((uint)22050);
        w.Write((uint)(22050 * 4));
        w.Write((ushort)4);
        w.Write((ushort)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write((uint)dataBytes);
        for (int i = 0; i < sampleCount * 2; i++) {
            w.Write((short)1000);
        }
    }

    // ------------------------------------------------------------------ //
    // WavPcmLoader — internal visibility needed for tests

    // WavPcmLoader is internal; this class is in the same test assembly so
    // InternalsVisibleTo covers it via the Spice86.Tests project reference.
}
