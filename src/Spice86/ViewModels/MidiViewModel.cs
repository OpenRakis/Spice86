namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

public partial class MidiViewModel : ViewModelBase {
    [ObservableProperty]
    private MidiInfo _midi = new();

    private readonly Midi _externalMidiDevice;

    public MidiViewModel(Midi externalMidiDevice, IUIDispatcherTimerFactory dispatcherTimerFactory) {
        _externalMidiDevice = externalMidiDevice;
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        Midi.LastPortRead = _externalMidiDevice.LastPortRead;
        Midi.LastPortWritten = _externalMidiDevice.LastPortWritten;
        Midi.LastPortWrittenValue = _externalMidiDevice.LastPortWrittenValue;
    }
}