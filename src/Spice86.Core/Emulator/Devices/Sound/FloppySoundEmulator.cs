namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Emulates floppy-drive audio (head-seek and motor-spin sounds) via the
/// software mixer, using real audio samples loaded from WAV files.
/// </summary>
/// <remarks>
/// <para>
/// Ported from DOSBox Staging's disk noise emulation
/// (<c>src/audio/disk_noise.cpp</c>).  The floppy-specific behaviour is:
/// </para>
/// <list type="bullet">
///   <item>Motor spin sound is played once per activation and is not looped.</item>
///   <item>Up to 9 seek samples are loaded; the selection algorithm gives an 80%
///         probability to the first two samples and 20% to the rest.</item>
///   <item>Sequential seek detection: when the same file path is accessed twice
///         in a row, the first two samples are always used.</item>
/// </list>
/// <para>
/// Sample files are looked up in the following order for each name:
/// <list type="number">
///   <item>The directory supplied via <paramref name="samplesDirectory"/> (if not null/empty).</item>
///   <item><c>&lt;AppContext.BaseDirectory&gt;/resources/disk_noises/</c></item>
///   <item>The process working directory.</item>
/// </list>
/// If none of the locations contain the file the corresponding sound is
/// silently skipped — the emulator continues to work without that sound.
/// </para>
/// <para>
/// <b>Sample file names</b> (22 050 Hz mono 16-bit PCM WAV):
/// <c>fdd_spinup.wav</c>, <c>fdd_spin.wav</c>,
/// <c>fdd_seek1.wav</c> … <c>fdd_seek9.wav</c>.
/// </para>
/// </remarks>
public sealed class FloppySoundEmulator {
    private const int SampleRateHz = 22050;
    private const int MaxSeekSamples = 9;
    private const string ResourceSubDir = "resources/disk_noises";

    private readonly SoundChannel _channel;
    private readonly FloppyDiskNoiseDevice _device;

    /// <summary>Gets the underlying sound channel (used by unit tests).</summary>
    internal SoundChannel Channel => _channel;

    /// <summary>
    /// Initialises the emulator with the default noise mode
    /// (<see cref="FloppyDiskNoiseMode.On"/>) and no user-specified samples directory.
    /// Sample files are resolved automatically via the standard search paths.
    /// </summary>
    /// <param name="mixer">The software mixer to register the floppy channel with.</param>
    public FloppySoundEmulator(SoftwareMixer mixer)
        : this(mixer, FloppyDiskNoiseMode.On, samplesDirectory: null) {
    }

    /// <summary>
    /// Initialises the emulator with an explicit noise mode and optional
    /// user-supplied samples directory.
    /// </summary>
    /// <param name="mixer">The software mixer to register the floppy channel with.</param>
    /// <param name="mode">The floppy noise emulation level.</param>
    /// <param name="samplesDirectory">
    /// Optional path to a directory containing the WAV sample files.
    /// Pass <see langword="null"/> or empty to rely on the automatic search.
    /// </param>
    public FloppySoundEmulator(SoftwareMixer mixer, FloppyDiskNoiseMode mode, string? samplesDirectory) {
        _channel = mixer.AddChannel(AudioCallback, SampleRateHz, "Floppy",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio });
        _channel.Enable(false);

        string spinUpPath = ResolveFile("fdd_spinup.wav", samplesDirectory);
        string spinPath = ResolveFile("fdd_spin.wav", samplesDirectory);
        List<string> seekPaths = new(MaxSeekSamples);
        for (int i = 1; i <= MaxSeekSamples; i++) {
            seekPaths.Add(ResolveFile($"fdd_seek{i}.wav", samplesDirectory));
        }

        _device = new FloppyDiskNoiseDevice(mode, spinUpPath, spinPath, seekPaths);
    }

    /// <summary>
    /// Triggers a head-seek noise burst and activates the motor spin sound.
    /// Called by the BIOS INT 13h handler on every floppy read or write.
    /// </summary>
    public void PlaySeek() {
        _device.ActivateSpin();
        _device.PlaySeek();
        UpdateChannelEnable();
    }

    /// <summary>
    /// Activates the motor spin sound without triggering a seek.
    /// Called when the FDC motor-enable bit is set.
    /// </summary>
    public void StartMotor() {
        _device.ActivateSpin();
        UpdateChannelEnable();
    }

    /// <summary>
    /// Stops the motor sound. If no seek sample is currently playing the
    /// mixer channel is disabled.
    /// </summary>
    public void StopMotor() {
        UpdateChannelEnable();
    }

    /// <summary>
    /// Updates the sequential/random seek detection state.
    /// Should be called by the FDC or BIOS layer with the image file path
    /// and the direction of the operation.
    /// </summary>
    /// <param name="path">The image file path being accessed.</param>
    /// <param name="isWrite"><see langword="true"/> for writes, <see langword="false"/> for reads.</param>
    public void SetLastIoPath(string path, bool isWrite) {
        _device.SetLastIoPath(path, isWrite);
    }

    // ------------------------------------------------------------------ //

    private void AudioCallback(int framesRequested) {
        // Stereo-interleaved output (L + R per frame)
        float[] buf = new float[framesRequested * 2];
        for (int i = 0; i < framesRequested; i++) {
            float sample = Math.Clamp(_device.GetNextFrame(), -1f, 1f);
            buf[i * 2] = sample;
            buf[i * 2 + 1] = sample;
        }

        if (!_device.IsActive) {
            _channel.Enable(false);
        }

        _channel.AddSamplesFloat(framesRequested, buf.AsSpan());
    }

    private void UpdateChannelEnable() {
        if (_device.IsActive) {
            _channel.Enable(true);
        } else {
            _channel.Enable(false);
        }
    }

    /// <summary>
    /// Resolves the path to a sample WAV file, searching the user-supplied
    /// directory first, then the built-in resources sub-directory, then the
    /// process working directory.  Returns the first existing path found, or
    /// the filename alone when no match is found (so the device skips it
    /// silently at load time).
    /// </summary>
    private static string ResolveFile(string filename, string? userDirectory) {
        if (!string.IsNullOrEmpty(userDirectory)) {
            string candidate = Path.Combine(userDirectory, filename);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        string builtinPath = Path.Combine(AppContext.BaseDirectory, ResourceSubDir, filename);
        if (File.Exists(builtinPath)) {
            return builtinPath;
        }

        string cwdPath = Path.Combine(Environment.CurrentDirectory, filename);
        if (File.Exists(cwdPath)) {
            return cwdPath;
        }

        // Return the filename so WavPcmLoader silently returns empty for it
        return filename;
    }
}
