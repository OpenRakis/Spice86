namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.ViewModels.Services;

using System;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>
/// View model for the mixer window, displaying and controlling mixer channels.
/// </summary>
public partial class MixerViewModel : ViewModelBase {
    private readonly Mixer _mixer;

    /// <summary>
    /// Collection of mixer channel view models.
    /// </summary>
    public ObservableCollection<MixerChannelViewModel> Channels { get; } = new();

    public MixerViewModel(Mixer mixer) {
        _mixer = mixer;

        // Start dispatcher timer to update channel state
        DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromMilliseconds(400),
            DispatcherPriority.Background,
            OnTimerTick);

        RefreshChannels();
    }

    private void OnTimerTick(object? sender, EventArgs e) {
        RefreshChannels();
    }

    [RelayCommand]
    private void ToggleChannel(MixerChannelViewModel channel) {
        if (channel != null) {
            channel.IsEnabled = !channel.IsEnabled;
        }
    }

    [RelayCommand]
    private void ToggleMute(MixerChannelViewModel channel) {
        if (channel != null) {
            channel.IsMuted = !channel.IsMuted;
        }
    }

    private void RefreshChannels() {
        // Get all channels from mixer
        System.Collections.Generic.List<MixerChannel> currentChannels = [.. _mixer.GetAllChannels()];

        // Remove channels that no longer exist
        for (int i = Channels.Count - 1; i >= 0; i--) {
            if (!currentChannels.Contains(Channels[i].GetChannel())) {
                Channels.RemoveAt(i);
            }
        }

        // Add new channels and update existing ones
        foreach (MixerChannel channel in currentChannels) {
            MixerChannelViewModel? existingVm = Channels.FirstOrDefault(vm => vm.GetChannel() == channel);
            if (existingVm != null) {
                existingVm.UpdateFromChannel();
            } else {
                Channels.Add(new MixerChannelViewModel(channel));
            }
        }
    }
}
