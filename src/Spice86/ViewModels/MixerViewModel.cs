namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

using System.ComponentModel;

public partial class MixerViewModel : ViewModelBase {

    [ObservableProperty]
    private AvaloniaList<SoundChannelInfo> _channels = new();

    private Dictionary<SoundChannel, SoundChannelInfo> _channelInfos = new();

    [RelayCommand]
    private void ResetStereoSeparation(object? parameter) {
        if(parameter is SoundChannelInfo info && _channelInfos.FirstOrDefault(x => x.Value == info).Key is SoundChannel channel) {
            channel.StereoSeparation = info.StereoSeparation = 50;
        }
    }

    private SoftwareMixer? _mixer;

    public MixerViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory) {
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateChannels);
    }

    private void UpdateChannels(object? sender, EventArgs e) {
        if (_mixer == null) {
            return;
        }
        foreach (SoundChannel channel in _mixer.Channels.Keys) {
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
        if(sender is SoundChannelInfo info && _channelInfos.FirstOrDefault(x => x.Value == info).Key is SoundChannel channel) {
            channel.IsMuted = info.IsMuted;
            channel.Volume = info.Volume;
            channel.StereoSeparation = info.StereoSeparation;
        }
    }

    public void VisitSoundMixer(SoftwareMixer mixer) {
        _mixer ??= mixer;
    }
}