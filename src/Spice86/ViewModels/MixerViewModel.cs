namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Devices.Video.Registers;
using Spice86.Core.Emulator.Memory;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

public partial class MixerViewModel : ViewModelBase {

    [ObservableProperty]
    private AvaloniaList<SoundChannelInfo> _channels = new();

    private Dictionary<SoundChannel, SoundChannelInfo> _channelInfos = new();

    private SoftwareMixer? _mixer;

    public MixerViewModel(IUIDispatcherTimer dispatcherTimer) {
        dispatcherTimer.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateChannels);
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
                info.PropertyChanged += (_, _) => {
                    channel.IsMuted = info.IsMuted;
                    channel.Volume = info.Volume;
                    channel.StereoSeparation = info.StereoSeparation;
                };
            } else {
                info.IsMuted = channel.IsMuted;
                info.Volume = channel.Volume;
                info.StereoSeparation = channel.StereoSeparation;
            }
            
        }
    }

    public void VisitSoundMixer(SoftwareMixer mixer) {
        _mixer ??= mixer;
    }
}