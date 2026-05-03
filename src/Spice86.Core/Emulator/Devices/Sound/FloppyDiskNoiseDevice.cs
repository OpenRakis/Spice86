namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;

/// <summary>
/// Models the disk-noise state machine for a single floppy drive.
/// </summary>
/// <remarks>
/// Ported from DOSBox Staging's <c>DiskNoiseDevice</c> class
/// (<c>src/audio/disk_noise.h</c> / <c>disk_noise.cpp</c>).
/// <para>
/// Behaviour differences from the HDD variant that are specific to floppy
/// drives (and mirrored here):
/// <list type="bullet">
///   <item>Spin sound is <b>not</b> looped — the motor runs for one pass then stops.</item>
///   <item>Seek sample selection: 80% chance of using sample[0] or sample[1];
///         20% chance of using any other loaded sample.</item>
///   <item>Sequential seek detection: when the same file path is accessed
///         twice in a row the seek type is set to
///         <see cref="FloppySeekType.Sequential"/>, which always picks from
///         the first two samples.</item>
/// </list>
/// </para>
/// <para>
/// Audio output: <see cref="GetNextFrame"/> must be called once per output
/// sample by the mixer callback.  All PCM samples are normalised floats in
/// [-1, 1]; a gain of 0.2 is applied when mixing (matching DOSBox Staging's
/// <c>DiskNoiseGain = 0.2f</c>).
/// </para>
/// </remarks>
internal sealed class FloppyDiskNoiseDevice {
    // Mirrors DiskNoiseGain in DOSBox Staging
    private const float DiskNoiseGain = 0.2f;

    // Maximum number of seek samples (DOSBox Staging uses up to 9)
    private const int MaxSeekSamples = 9;

    // 80% preference for first two samples in random-access seek
    private const int FirstTwoSamplesWeight = 8;
    private const int TotalWeightBuckets = 10;

    private readonly FloppyDiskNoiseMode _mode;
    private readonly Random _random = new();
    private readonly object _lock = new();

    // ---- spin state ----
    private float[] _spinUpSamples = Array.Empty<float>();
    private float[] _spinSamples = Array.Empty<float>();
    private int _spinUpPos;
    private int _spinPos;

    // ---- seek state ----
    private readonly List<float[]> _seekSamples = new();
    private float[] _currentSeekSample = Array.Empty<float>();
    private int _currentSeekPos;

    // ---- sequential-seek detection ----
    private FloppySeekType _seekType = FloppySeekType.RandomAccess;
    private string _lastReadPath = string.Empty;
    private string _lastWritePath = string.Empty;

    /// <summary>
    /// Initialises the device and loads all audio samples.
    /// </summary>
    /// <param name="mode">The noise emulation level.</param>
    /// <param name="spinUpPath">Path to the spin-up WAV (one-shot, played once when the motor first activates).</param>
    /// <param name="spinPath">Path to the spin-sustain WAV (played once per motor activation after spin-up).</param>
    /// <param name="seekPaths">Up to <see cref="MaxSeekSamples"/> paths to seek-noise WAV files.</param>
    internal FloppyDiskNoiseDevice(FloppyDiskNoiseMode mode, string spinUpPath, string spinPath, IReadOnlyList<string> seekPaths) {
        _mode = mode;
        if (_mode == FloppyDiskNoiseMode.Off) {
            return;
        }

        _spinUpSamples = WavPcmLoader.TryLoad(spinUpPath);
        _spinSamples = WavPcmLoader.TryLoad(spinPath);

        int count = Math.Min(seekPaths.Count, MaxSeekSamples);
        for (int i = 0; i < count; i++) {
            float[] samples = WavPcmLoader.TryLoad(seekPaths[i]);
            _seekSamples.Add(samples);
        }

        ResetIterators();
    }

    /// <summary>
    /// Returns the next mixed audio frame (single mono sample, pre-gain-applied).
    /// Call once per output sample from the mixer callback.
    /// </summary>
    internal float GetNextFrame() {
        lock (_lock) {
            float sample = 0f;

            // --- spin-up phase ---
            if (_spinUpPos < _spinUpSamples.Length) {
                sample += _spinUpSamples[_spinUpPos] * DiskNoiseGain;
                _spinUpPos++;
            } else if (_spinPos < _spinSamples.Length) {
                // --- spin-sustain phase (non-looping for floppy) ---
                sample += _spinSamples[_spinPos] * DiskNoiseGain;
                _spinPos++;
            }

            // --- seek phase ---
            if (_currentSeekPos < _currentSeekSample.Length) {
                sample += _currentSeekSample[_currentSeekPos] * DiskNoiseGain;
                _currentSeekPos++;

                // Seek sample finished — clear it
                if (_currentSeekPos >= _currentSeekSample.Length) {
                    _currentSeekSample = Array.Empty<float>();
                    _currentSeekPos = 0;
                }
            }

            return sample;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when any audio is currently playing
    /// (spin-up, spin-sustain, or seek sample not yet exhausted).
    /// </summary>
    internal bool IsActive {
        get {
            lock (_lock) {
                return _spinUpPos < _spinUpSamples.Length
                    || _spinPos < _spinSamples.Length
                    || _currentSeekPos < _currentSeekSample.Length;
            }
        }
    }

    /// <summary>
    /// Activates the motor spin sound.
    /// Mirrors <c>DiskNoiseDevice::ActivateSpin()</c> in DOSBox Staging.
    /// The spin is only (re-)started once the previous playback has
    /// finished — it is not looped.
    /// </summary>
    internal void ActivateSpin() {
        lock (_lock) {
            if (_mode == FloppyDiskNoiseMode.Off) {
                return;
            }

            // Check if the spin sample is still playing; do not interrupt it
            bool spinUpStillPlaying = _spinUpPos < _spinUpSamples.Length;
            bool spinStillPlaying = _spinPos < _spinSamples.Length;
            if (spinUpStillPlaying || spinStillPlaying) {
                return;
            }

            // Restart spin from the beginning
            _spinUpPos = 0;
            _spinPos = 0;
        }
    }

    /// <summary>
    /// Triggers a head-seek noise burst.
    /// Mirrors <c>DiskNoiseDevice::PlaySeek()</c> in DOSBox Staging.
    /// If a seek sample is still playing it is not interrupted.
    /// </summary>
    internal void PlaySeek() {
        lock (_lock) {
            if (_mode is FloppyDiskNoiseMode.Off) {
                return;
            }

            // Do not interrupt a seek sample that is still playing
            if (_currentSeekPos < _currentSeekSample.Length) {
                return;
            }

            int index = ChooseSeekIndex();
            if (index >= _seekSamples.Count || _seekSamples[index].Length == 0) {
                return;
            }

            _currentSeekSample = _seekSamples[index];
            _currentSeekPos = 0;
        }
    }

    /// <summary>
    /// Updates the sequential/random seek detection state based on the
    /// last accessed file path.
    /// Mirrors <c>DiskNoiseDevice::SetLastIoPath()</c> in DOSBox Staging.
    /// </summary>
    /// <param name="path">The host path of the file that was accessed.</param>
    /// <param name="isWrite"><see langword="true"/> for write operations, <see langword="false"/> for reads.</param>
    internal void SetLastIoPath(string path, bool isWrite) {
        if (_mode == FloppyDiskNoiseMode.Off || string.IsNullOrEmpty(path)) {
            return;
        }

        if (isWrite) {
            _seekType = string.Equals(path, _lastWritePath, StringComparison.Ordinal)
                ? FloppySeekType.Sequential
                : FloppySeekType.RandomAccess;
            _lastWritePath = path;
        } else {
            _seekType = string.Equals(path, _lastReadPath, StringComparison.Ordinal)
                ? FloppySeekType.Sequential
                : FloppySeekType.RandomAccess;
            _lastReadPath = path;
        }
    }

    // ------------------------------------------------------------------ //

    private void ResetIterators() {
        _spinUpPos = _spinUpSamples.Length; // exhausted — motor is idle initially
        _spinPos = _spinSamples.Length;
        _currentSeekPos = _currentSeekSample.Length;
    }

    /// <summary>
    /// Chooses the index of the seek sample to play, using the same
    /// weighted-random algorithm as DOSBox Staging.
    /// </summary>
    private int ChooseSeekIndex() {
        if (_seekSamples.Count == 0) {
            return 0;
        }

        // Sequential seek: always use one of the first two samples
        if (_seekType == FloppySeekType.Sequential) {
            if (_seekSamples.Count == 1) {
                return 0;
            }

            return _random.Next(0, 2);
        }

        // Random-access seek with only 1 or 2 samples — no special weighting needed
        if (_seekSamples.Count <= 2) {
            return _random.Next(0, _seekSamples.Count);
        }

        // Random-access seek with 3+ samples:
        // 80% chance of picking sample[0] or sample[1]
        if (_random.Next(0, TotalWeightBuckets) < FirstTwoSamplesWeight) {
            return _random.Next(0, 2);
        }

        // 20% chance: pick uniformly from samples[2..N-1]
        return _random.Next(2, _seekSamples.Count);
    }
}
