namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Audio.Mixer;
using Spice86.Audio.Sound.Common;

using System;
using System.Collections.Generic;

/// <summary>
/// View model wrapping a single MixerChannel for display
/// </summary>
public partial class MixerChannelViewModel : ViewModelBase {
    private readonly MixerChannel _channel;

    // Peak level tracking with decay (UI-side calculation, no impact on core)
    // Decay rate per update tick (at 50ms updates, this gives smooth falloff)
    private const double PeakDecayRate = 0.75;
    // Normalization factor for 16-bit audio samples
    private const double SampleNormalizationFactor = 1.0 / 32768.0;
    // Amplification to make typical audio levels more visible on the meter
    private const double SignalAmplification = 2.5;
    private double _currentPeakLeft;
    private double _currentPeakRight;

    // Waveform display buffer settings
    // Display approximately 2 seconds of audio, downsampled for display efficiency
    private const int WaveformDisplaySamples = 400; // ~2 seconds at 50ms updates with ~4 samples per update
    private const int WaveformDownsampleFactor = 128; // Downsample factor for efficiency
    private readonly float[] _waveformBufferLeft = new float[WaveformDisplaySamples];
    private readonly float[] _waveformBufferRight = new float[WaveformDisplaySamples];
    private int _waveformWriteIndex;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private double _userVolumeLeftPercent;

    [ObservableProperty]
    private double _userVolumeRightPercent;

    [ObservableProperty]
    private double _appVolumeLeftPercent;

    [ObservableProperty]
    private double _appVolumeRightPercent;

    [ObservableProperty]
    private int _sampleRate;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _peakLevelLeft;

    [ObservableProperty]
    private double _peakLevelRight;

    [ObservableProperty]
    private IReadOnlyList<float>? _waveformSamplesLeft;

    [ObservableProperty]
    private IReadOnlyList<float>? _waveformSamplesRight;

    public MixerChannelViewModel(MixerChannel channel) {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        UpdateFromChannel();
    }

    public MixerChannel Channel => _channel;

    public void UpdateFromChannel() {
        Name = _channel.Name;
        IsEnabled = _channel.IsEnabled;
        SampleRate = _channel.SampleRate;
        AudioFrame userVolume = _channel.UserVolume;
        UserVolumeLeftPercent = userVolume.Left * 100.0;
        UserVolumeRightPercent = userVolume.Right * 100.0;
        AudioFrame appVolume = _channel.AppVolume;
        AppVolumeLeftPercent = appVolume.Left * 100.0;
        AppVolumeRightPercent = appVolume.Right * 100.0;
        IsMuted = userVolume.Left == 0.0f && userVolume.Right == 0.0f;
        UpdatePeakLevels();
    }

    private void UpdatePeakLevels() {
        _currentPeakLeft *= PeakDecayRate;
        _currentPeakRight *= PeakDecayRate;

        AudioFrameBuffer audioFrames = _channel.AudioFrames;
        int frameCount = audioFrames.Count;

        double maxLeft = 0.0;
        double maxRight = 0.0;

        float waveformSampleLeft = 0.0f;
        float waveformSampleRight = 0.0f;
        int waveformSampleCount = 0;

        for (int i = 0; i < frameCount; i += 2) {
            AudioFrame frame = audioFrames[i];

            double absLeft = Math.Abs(frame.Left);
            double absRight = Math.Abs(frame.Right);

            if (absLeft > maxLeft) {
                maxLeft = absLeft;
            }
            if (absRight > maxRight) {
                maxRight = absRight;
            }

            waveformSampleLeft += frame.Left;
            waveformSampleRight += frame.Right;
            waveformSampleCount++;

            if (waveformSampleCount >= WaveformDownsampleFactor) {
                float avgLeft = (float)(waveformSampleLeft / waveformSampleCount * SampleNormalizationFactor);
                float avgRight = (float)(waveformSampleRight / waveformSampleCount * SampleNormalizationFactor);

                _waveformBufferLeft[_waveformWriteIndex] = Math.Clamp(avgLeft, -1.0f, 1.0f);
                _waveformBufferRight[_waveformWriteIndex] = Math.Clamp(avgRight, -1.0f, 1.0f);
                _waveformWriteIndex = (_waveformWriteIndex + 1) % WaveformDisplaySamples;

                waveformSampleLeft = 0.0f;
                waveformSampleRight = 0.0f;
                waveformSampleCount = 0;
            }
        }

        if (waveformSampleCount > 0) {
            float avgLeft = (float)(waveformSampleLeft / waveformSampleCount * SampleNormalizationFactor);
            float avgRight = (float)(waveformSampleRight / waveformSampleCount * SampleNormalizationFactor);

            _waveformBufferLeft[_waveformWriteIndex] = Math.Clamp(avgLeft, -1.0f, 1.0f);
            _waveformBufferRight[_waveformWriteIndex] = Math.Clamp(avgRight, -1.0f, 1.0f);
            _waveformWriteIndex = (_waveformWriteIndex + 1) % WaveformDisplaySamples;
        }

        double normalizedLeft = maxLeft * SampleNormalizationFactor * SignalAmplification;
        double normalizedRight = maxRight * SampleNormalizationFactor * SignalAmplification;

        if (normalizedLeft > _currentPeakLeft) {
            _currentPeakLeft = normalizedLeft;
        }
        if (normalizedRight > _currentPeakRight) {
            _currentPeakRight = normalizedRight;
        }
        PeakLevelLeft = Math.Clamp(_currentPeakLeft, 0.0, 1.0);
        PeakLevelRight = Math.Clamp(_currentPeakRight, 0.0, 1.0);
        UpdateWaveformDisplay();
    }

    private void UpdateWaveformDisplay() {
        float[] linearLeft = new float[WaveformDisplaySamples];
        float[] linearRight = new float[WaveformDisplaySamples];

        for (int i = 0; i < WaveformDisplaySamples; i++) {
            int bufferIndex = (_waveformWriteIndex + i) % WaveformDisplaySamples;
            linearLeft[i] = _waveformBufferLeft[bufferIndex];
            linearRight[i] = _waveformBufferRight[bufferIndex];
        }
        WaveformSamplesLeft = linearLeft;
        WaveformSamplesRight = linearRight;
    }

    partial void OnIsEnabledChanged(bool value) {
        _channel.Enable(value);
    }

    partial void OnIsMutedChanged(bool value) {
        if (value) {
            _channel.UserVolume = new AudioFrame(0.0f, 0.0f);
            UserVolumeLeftPercent = 0.0;
            UserVolumeRightPercent = 0.0;
        } else {
            _channel.UserVolume = new AudioFrame(1.0f, 1.0f);
            UserVolumeLeftPercent = 100.0;
            UserVolumeRightPercent = 100.0;
        }
    }

    partial void OnUserVolumeLeftPercentChanged(double value) {
        AudioFrame current = _channel.UserVolume;
        _channel.UserVolume = new AudioFrame((float)(value / 100.0), current.Right);
    }

    partial void OnUserVolumeRightPercentChanged(double value) {
        AudioFrame current = _channel.UserVolume;
        _channel.UserVolume = new AudioFrame(current.Left, (float)(value / 100.0));
    }
}