namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
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

    /// <summary>
    /// Gets the audio settings sub-viewmodel.
    /// </summary>
    public AudioSettingsViewModel AudioSettings { get; }

    public MixerViewModel(Mixer mixer, SoundBlaster soundBlaster, Opl opl) {
        _mixer = mixer;
        AudioSettings = new AudioSettingsViewModel(soundBlaster, opl);

        // Start dispatcher timer to update channel state
        // Use 50ms for near real-time VU meter feedback
        DispatcherTimerStarter.StartNewDispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Background,
            OnTimerTick);

        RefreshChannels();
    }

    private void OnTimerTick(object? sender, EventArgs e) {
        RefreshChannels();
    }

    [RelayCommand]
    private void ToggleChannel(MixerChannelViewModel channel) {
        channel?.IsEnabled = !channel.IsEnabled;
    }

    [RelayCommand]
    private void ToggleMute(MixerChannelViewModel channel) {
        channel?.IsMuted = !channel.IsMuted;
    }

    private void RefreshChannels() {
        List<MixerChannel> currentChannels = [.. _mixer.AllChannels];

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
