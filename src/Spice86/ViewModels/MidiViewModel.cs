namespace Spice86.ViewModels;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Models.Debugging;

public partial class MidiViewModel : ViewModelBase, IInternalDebugger {
    [ObservableProperty]
    private MidiInfo _midi = new();

    private Midi? _externalMidiDevice;

    public MidiViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory) {
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, UpdateValues);
    }

    private void UpdateValues(object? sender, EventArgs e) {
        if (_externalMidiDevice is null) {
            return;
        }
        Midi.LastPortRead = _externalMidiDevice.LastPortRead;
        Midi.LastPortWritten = _externalMidiDevice.LastPortWritten;
        Midi.LastPortWrittenValue = _externalMidiDevice.LastPortWrittenValue;
    }

    public void Visit<T>(T component) where T : IDebuggableComponent {
        _externalMidiDevice ??= component as Midi;
    }
    
    public bool NeedsToVisitEmulator => _externalMidiDevice is null;
}