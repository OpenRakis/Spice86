namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

using System.ComponentModel;

public partial class SoftwareMixerViewModel : ViewModelBase {
    private readonly Dictionary<SoundChannel, SoundChannelInfo> _channelInfos = new();
    private readonly SoftwareMixer _softwareMixer;
    
    [ObservableProperty]
    private AvaloniaList<SoundChannelInfo> _channels = new();
    
    public SoftwareMixerViewModel(SoftwareMixer softwareMixer) {
        _softwareMixer = softwareMixer;
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        UpdateChannels(_softwareMixer);
    }

    [RelayCommand]
    private void ResetStereoSeparation(object? parameter) {
        if(parameter is SoundChannelInfo info && _channelInfos.FirstOrDefault(x => x.Value == info).Key is { } channel) {
            channel.StereoSeparation = info.StereoSeparation = 50;
        }
    }
    
    private void UpdateChannels(SoftwareMixer mixer) {
        foreach (SoundChannel channel in mixer.Channels.Keys) {
            if (!_channelInfos.TryGetValue(channel, out SoundChannelInfo? info)) {
                info = new SoundChannelInfo() {
                    Name = channel.Name
                };
                _channelInfos.Add(channel, info);
                Channels.Add(info);
                info.PropertyChanged += OnChannelPropertyChanged;
            } else {
                info.PropertyChanged -= OnChannelPropertyChanged;
                info.IsMuted = channel.IsMuted;
                info.Volume = channel.Volume;
                info.StereoSeparation = channel.StereoSeparation;
                info.PropertyChanged += OnChannelPropertyChanged;
            }
        }
    }

    private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is not SoundChannelInfo info ||
            _channelInfos.FirstOrDefault(x => x.Value == info).Key is not { } channel) {
            return;
        }

        channel.IsMuted = info.IsMuted;
        channel.Volume = info.Volume;
        channel.StereoSeparation = info.StereoSeparation;
    }
}