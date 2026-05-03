namespace Spice86.Core.Emulator.Devices.Sound;

using System;
using System.Collections.Generic;

/// <summary>
/// Synthesizes floppy drive sounds (seek noise and motor hum) via the software mixer.
/// </summary>
public sealed class FloppySoundEmulator {
    private const int SampleRateHz = 22050;
    private const int SeekDurationSamples = SampleRateHz / 20; // 50 ms

    private readonly SoundChannel _channel;
    private readonly Random _random = new(42);

    /// <summary>Gets the underlying sound channel for test introspection.</summary>
    internal SoundChannel Channel => _channel;

    private bool _motorRunning;
    private int _motorSamplePos;
    private int _seekSamplesRemaining;

    /// <summary>
    /// Initialises a new <see cref="FloppySoundEmulator"/> and registers an audio channel with the mixer.
    /// </summary>
    /// <param name="mixer">The software mixer to register the floppy channel with.</param>
    public FloppySoundEmulator(SoftwareMixer mixer) {
        _channel = mixer.AddChannel(AudioCallback, SampleRateHz, "Floppy",
            new HashSet<ChannelFeature> { ChannelFeature.DigitalAudio });
        _channel.Enable(false);
    }

    /// <summary>Triggers a short seek noise burst (approximately 50 ms of decaying white noise).</summary>
    public void PlaySeek() {
        _seekSamplesRemaining = SeekDurationSamples;
        _channel.Enable(true);
    }

    /// <summary>Starts the motor hum (low-frequency sine wave).</summary>
    public void StartMotor() {
        _motorRunning = true;
        _channel.Enable(true);
    }

    /// <summary>Stops the motor hum. Disables the channel if no seek is in progress.</summary>
    public void StopMotor() {
        _motorRunning = false;
        if (_seekSamplesRemaining <= 0) {
            _channel.Enable(false);
        }
    }

    private void AudioCallback(int framesRequested) {
        // AddSamplesFloat expects stereo interleaved data (L, R per frame).
        float[] buf = new float[framesRequested * 2];
        for (int i = 0; i < framesRequested; i++) {
            float sample = 0f;

            if (_seekSamplesRemaining > 0) {
                float envelope = (float)_seekSamplesRemaining / SeekDurationSamples;
                float noise = (float)(_random.NextDouble() * 2.0 - 1.0);
                sample += noise * envelope * 0.35f;
                _seekSamplesRemaining--;
            }

            if (_motorRunning) {
                double t = (double)_motorSamplePos / SampleRateHz;
                sample += (float)(Math.Sin(2.0 * Math.PI * 80.0 * t) * 0.15
                                + Math.Sin(2.0 * Math.PI * 160.0 * t) * 0.05);
                _motorSamplePos = (_motorSamplePos + 1) % SampleRateHz;
            }

            float clamped = Math.Clamp(sample, -1f, 1f);
            buf[i * 2] = clamped;
            buf[i * 2 + 1] = clamped;
        }

        if (_seekSamplesRemaining <= 0 && !_motorRunning) {
            _channel.Enable(false);
        }

        _channel.AddSamplesFloat(framesRequested, buf.AsSpan());
    }
}
