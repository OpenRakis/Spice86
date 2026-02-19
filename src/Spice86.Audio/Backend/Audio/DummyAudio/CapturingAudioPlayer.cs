namespace Spice86.Audio.Backend.Audio.DummyAudio;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

/// <summary>
/// Audio player that captures all mixer output into memory for analysis.
/// Extends the DummyAudioPlayer behavior (backpressure simulation) while
/// recording every sample written by the mixer thread.
/// </summary>
public sealed class CapturingAudioPlayer : AudioPlayer {
    private readonly List<float> _capturedSamples = new();
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CapturingAudioPlayer"/> class.
    /// </summary>
    /// <param name="format">The audio playback format.</param>
    public CapturingAudioPlayer(AudioFormat format) : base(format) {
    }

    /// <summary>
    /// Writes audio data while capturing it for later analysis.
    /// Simulates real audio device backpressure by sleeping for the expected
    /// playback duration, preventing the mixer thread from racing far ahead
    /// of the emulation thread and producing thousands of silence blocks
    /// before the CPU has time to program the sound hardware.
    /// </summary>
    /// <param name="data">The input audio data (interleaved stereo float samples).</param>
    /// <returns>The data parameter length.</returns>
    public override int WriteData(Span<float> data) {
        lock (_lock) {
            for (int i = 0; i < data.Length; i++) {
                _capturedSamples.Add(data[i]);
            }
        }
        // Simulate real audio device backpressure: sleep for the expected
        // playback duration, just like DummyAudioPlayer and the real
        // CrossPlatformAudioPlayer (which blocks in BulkEnqueue).
        int sampleRate = Format.SampleRate;
        int channels = Format.Channels;
        if (sampleRate > 0 && channels > 0) {
            int frames = data.Length / channels;
            int sleepMs = (int)((long)frames * 1000 / sampleRate);
            if (sleepMs > 0) {
                Thread.Sleep(sleepMs);
            }
        }
        return data.Length;
    }

    /// <summary>
    /// Gets a copy of all captured audio samples (interleaved stereo).
    /// </summary>
    /// <returns>Array of interleaved float samples [L, R, L, R, ...].</returns>
    public float[] GetCapturedSamples() {
        lock (_lock) {
            return _capturedSamples.ToArray();
        }
    }

    /// <summary>
    /// Gets the total number of captured stereo frames.
    /// </summary>
    public int CapturedFrameCount {
        get {
            lock (_lock) {
                int channels = Format.Channels;
                return channels > 0 ? _capturedSamples.Count / channels : 0;
            }
        }
    }

    /// <inheritdoc/>
    public override void Start() {
    }

    /// <inheritdoc/>
    public override void ClearQueuedData() {
    }

    /// <inheritdoc/>
    public override void MuteOutput() {
    }

    /// <inheritdoc/>
    public override void UnmuteOutput() {
    }

    /// <summary>
    /// Saves the captured audio to a WAV file for manual inspection.
    /// </summary>
    /// <param name="filePath">The path to save the WAV file.</param>
    public void SaveToWav(string filePath) {
        float[] samples;
        lock (_lock) {
            samples = _capturedSamples.ToArray();
        }
        int sampleRate = Format.SampleRate;
        int channels = Format.Channels;
        int bitsPerSample = 32;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int dataSize = samples.Length * (bitsPerSample / 8);

        using FileStream fs = File.Create(filePath);
        using BinaryWriter bw = new(fs);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);

        // fmt chunk
        bw.Write("fmt "u8);
        bw.Write(16);           // chunk size
        bw.Write((short)3);     // IEEE float
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);
        foreach (float sample in samples) {
            bw.Write(sample);
        }
    }
}
