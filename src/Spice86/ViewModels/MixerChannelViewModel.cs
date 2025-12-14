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
    private double _stereoSeparation = 100.0;

    [ObservableProperty]
    private bool _isMuted;

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

        // Calculate stereo separation from channel map
        StereoLine channelMap = _channel.GetChannelMap();
        if (channelMap.Left == LineIndex.Left && channelMap.Right == LineIndex.Right) {
            StereoSeparation = 100.0; // Normal stereo
        } else if (channelMap.Left == LineIndex.Right && channelMap.Right == LineIndex.Left) {
            StereoSeparation = -100.0; // Reversed
        } else if (channelMap.Left == LineIndex.Left && channelMap.Right == LineIndex.Left) {
            StereoSeparation = 0.0; // Mono (left)
        } else if (channelMap.Left == LineIndex.Right && channelMap.Right == LineIndex.Right) {
            StereoSeparation = 0.0; // Mono (right)
        } else {
            StereoSeparation = 100.0; // Default
        }
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

    partial void OnStereoSeparationChanged(double value) {
        // Map separation percentage to channel mapping
        if (value >= 75.0) {
            // Normal stereo (100 to 75): L->L, R->R
            _channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });
        } else if (value >= 25.0) {
            // Reduced stereo (75 to 25): gradually mix
            // For simplicity, keep normal until we implement proper mixing
            _channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Right });
        } else if (value >= -25.0) {
            // Mono (25 to -25): both to both
            _channel.SetChannelMap(new StereoLine { Left = LineIndex.Left, Right = LineIndex.Left });
        } else if (value >= -75.0) {
            // Reduced reverse (−25 to −75)
            _channel.SetChannelMap(new StereoLine { Left = LineIndex.Right, Right = LineIndex.Left });
        } else {
            // Reversed stereo (−75 to −100): L->R, R->L
            _channel.SetChannelMap(new StereoLine { Left = LineIndex.Right, Right = LineIndex.Left });
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
