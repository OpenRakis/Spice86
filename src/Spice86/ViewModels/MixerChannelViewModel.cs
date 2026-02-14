namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Libs.Sound.Common;

using System;

/// <summary>
/// View model wrapping a single MixerChannel for display/editing.
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
    private string _features = string.Empty;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private double _peakLevelLeft;

    [ObservableProperty]
    private double _peakLevelRight;

    public MixerChannelViewModel(MixerChannel channel) {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        UpdateFromChannel();
    }

    public MixerChannel GetChannel() {
        return _channel;
    }

    public void UpdateFromChannel() {
        Name = _channel.GetName();
        IsEnabled = _channel.IsEnabled;
        SampleRate = _channel.GetSampleRate();

        AudioFrame userVolume = _channel.GetUserVolume();
        UserVolumeLeftPercent = userVolume.Left * 100.0;
        UserVolumeRightPercent = userVolume.Right * 100.0;

        AudioFrame appVolume = _channel.GetAppVolume();
        AppVolumeLeftPercent = appVolume.Left * 100.0;
        AppVolumeRightPercent = appVolume.Right * 100.0;

        // Muted is when both user volumes are zero
        IsMuted = userVolume.Left == 0.0f && userVolume.Right == 0.0f;

        Features = string.Join(", ", _channel.GetFeatures());

        // Update peak levels for VU meters (read-only access to AudioFrames, no core impact)
        UpdatePeakLevels();
    }

    /// <summary>
    /// Calculates peak levels from the channel's audio frame buffer.
    /// This is a UI-only calculation that reads from the public AudioFrames buffer
    /// without impacting the core mixing logic.
    /// </summary>
    private void UpdatePeakLevels() {
        // Apply decay to existing peaks (smooth falloff between updates)
        _currentPeakLeft *= PeakDecayRate;
        _currentPeakRight *= PeakDecayRate;

        // Read current audio frames (read-only snapshot access)
        AudioFrameBuffer audioFrames = _channel.AudioFrames;
        int frameCount = audioFrames.Count;

        // Find peak amplitude in current buffer
        double maxLeft = 0.0;
        double maxRight = 0.0;

        // Sample frames for peak detection (every 2nd frame for efficiency)
        for (int i = 0; i < frameCount; i += 2) {
            AudioFrame frame = audioFrames[i];

            // Get absolute amplitude
            double absLeft = Math.Abs(frame.Left);
            double absRight = Math.Abs(frame.Right);

            if (absLeft > maxLeft) {
                maxLeft = absLeft;
            }
            if (absRight > maxRight) {
                maxRight = absRight;
            }
        }

        // Normalize and amplify for better visual feedback
        double normalizedLeft = maxLeft * SampleNormalizationFactor * SignalAmplification;
        double normalizedRight = maxRight * SampleNormalizationFactor * SignalAmplification;

        // Update peaks if new values are higher (peak hold behavior)
        if (normalizedLeft > _currentPeakLeft) {
            _currentPeakLeft = normalizedLeft;
        }
        if (normalizedRight > _currentPeakRight) {
            _currentPeakRight = normalizedRight;
        }

        // Clamp and update observable properties
        PeakLevelLeft = Math.Clamp(_currentPeakLeft, 0.0, 1.0);
        PeakLevelRight = Math.Clamp(_currentPeakRight, 0.0, 1.0);
    }

    partial void OnIsEnabledChanged(bool value) {
        _channel.Enable(value);
    }

    partial void OnIsMutedChanged(bool value) {
        if (value) {
            // Mute: set user volume to zero
            _channel.SetUserVolume(new AudioFrame(0.0f, 0.0f));
            UserVolumeLeftPercent = 0.0;
            UserVolumeRightPercent = 0.0;
        } else {
            // Unmute: restore to 100%
            _channel.SetUserVolume(new AudioFrame(1.0f, 1.0f));
            UserVolumeLeftPercent = 100.0;
            UserVolumeRightPercent = 100.0;
        }
    }

    partial void OnUserVolumeLeftPercentChanged(double value) {
        AudioFrame current = _channel.GetUserVolume();
        _channel.SetUserVolume(new AudioFrame((float)(value / 100.0), current.Right));
    }

    partial void OnUserVolumeRightPercentChanged(double value) {
        AudioFrame current = _channel.GetUserVolume();
        _channel.SetUserVolume(new AudioFrame(current.Left, (float)(value / 100.0)));
    }
}
