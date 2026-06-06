namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

/// <summary>
/// Emulates floppy-drive audio (head-seek and motor-spin sounds) via the
/// software mixer, using real audio samples loaded from WAV files.
/// </summary>
public sealed class FloppySoundEmulator {
    private const int SampleRateHz = 22050;
    private const int MaxSeekSamples = 9;
    private const string ResourceSubDir = "resources/disk_noises";
    private const string EmbeddedResourcePrefix = "Spice86.Core.DiskNoises.";

    private readonly SoundChannel _channel;
    private readonly FloppyDiskNoiseDevice _device;
    private float[] _frameBuffer = Array.Empty<float>();

    /// <summary>Gets the underlying sound channel (used by unit tests).</summary>
    internal SoundChannel Channel => _channel;

    /// <summary>
    /// Initialises the emulator with an explicit noise mode and sample directory hint.
    /// </summary>
    /// <param name="channelCreator">The sound channel creator to register the floppy channel with.</param>
    /// <param name="mode">The floppy noise emulation level.</param>
    /// <param name="samplesDirectory">
    /// Path to a directory containing the WAV sample files. Pass an empty string to rely on automatic search.
    /// </param>
    public FloppySoundEmulator(ISoundChannelCreator channelCreator, FloppyDiskNoiseMode mode, string samplesDirectory) {
        _channel = channelCreator.AddChannel(AudioCallback, SampleRateHz, "Floppy",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio });
        _channel.Enable(false);

        float[] spinUpSamples = LoadSamples("fdd_spinup.wav", samplesDirectory);
        float[] spinSamples = LoadSamples("fdd_spin.wav", samplesDirectory);
        List<float[]> seekSamplesList = new(MaxSeekSamples);
        for (int i = 1; i <= MaxSeekSamples; i++) {
            seekSamplesList.Add(LoadSamples($"fdd_seek{i}.wav", samplesDirectory));
        }

        _device = new FloppyDiskNoiseDevice(mode, spinUpSamples, spinSamples, seekSamplesList);
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
    public void RecordLastIoPath(string path, bool isWrite) {
        _device.RecordLastIoPath(path, isWrite);
    }

    private void AudioCallback(int framesRequested) {
        // Stereo-interleaved output (L + R per frame)
        int needed = framesRequested * 2;
        if (_frameBuffer.Length < needed) {
            _frameBuffer = new float[needed];
        }
        for (int i = 0; i < framesRequested; i++) {
            float sample = Math.Clamp(_device.GetNextFrame(), -1f, 1f);
            _frameBuffer[i * 2] = sample;
            _frameBuffer[i * 2 + 1] = sample;
        }

        if (!_device.IsActive) {
            _channel.Enable(false);
        }

        _channel.AddSamplesNormalized(framesRequested, _frameBuffer.AsSpan(0, needed));
    }

    private void UpdateChannelEnable() {
        if (_device.IsActive) {
            _channel.Enable(true);
        } else {
            _channel.Enable(false);
        }
    }

    /// <summary>
    /// Loads WAV samples for the given filename, searching on disk first and
    /// then falling back to the embedded assembly resource.
    /// </summary>
    private static float[] LoadSamples(string filename, string userDirectory) {
        string resolvedPath = ResolveFilePath(filename, userDirectory);
        if (!string.IsNullOrEmpty(resolvedPath)) {
            float[] samples = WavPcmLoader.TryLoad(resolvedPath);
            if (samples.Length > 0) {
                return samples;
            }
        }

        return TryLoadFromEmbeddedResource(filename);
    }

    /// <summary>
    /// Resolves the on-disk path to a sample WAV file, searching the user-supplied
    /// directory first, then the built-in resources sub-directory, then the
    /// process working directory. Returns the first existing path found, or
    /// an empty string when no file is found on disk.
    /// </summary>
    private static string ResolveFilePath(string filename, string userDirectory) {
        if (!string.IsNullOrEmpty(userDirectory)) {
            string candidate = Path.Join(userDirectory, filename);
            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        string builtinPath = Path.Join(AppContext.BaseDirectory, ResourceSubDir, filename);
        if (File.Exists(builtinPath)) {
            return builtinPath;
        }

        string cwdPath = Path.Join(Environment.CurrentDirectory, filename);
        if (File.Exists(cwdPath)) {
            return cwdPath;
        }

        return string.Empty;
    }

    /// <summary>
    /// Attempts to load a WAV file from the embedded assembly resources.
    /// The resource name is <c>Spice86.Core.DiskNoises.&lt;filename&gt;</c>.
    /// </summary>
    private static float[] TryLoadFromEmbeddedResource(string filename) {
        string resourceName = EmbeddedResourcePrefix + filename;
        Assembly assembly = typeof(FloppySoundEmulator).Assembly;
        Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) {
            return Array.Empty<float>();
        }

        using (stream) {
            return WavPcmLoader.TryLoadFromStream(stream);
        }
    }
}
