namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.ViewModels.ValueViewModels.Debugging;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.Services;

public partial class MidiViewModel : ViewModelBase, IEmulatorObjectViewModel {
    [ObservableProperty]
    private MidiInfo _midi = new();

    private readonly Midi _externalMidiDevice;

    public MidiViewModel(Midi externalMidiDevice) {
        _externalMidiDevice = externalMidiDevice;
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, UpdateValues);
    }

    public bool IsVisible { get; set; }


    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }
        _externalMidiDevice.CopyToMidiInfo(Midi);
    }
}