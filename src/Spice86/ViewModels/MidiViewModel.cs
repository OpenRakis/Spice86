namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Models.Debugging;

public partial class MidiViewModel : ViewModelBase, IInternalDebugger {
    [ObservableProperty]
    private MidiInfo _midi = new();

    public void Visit<T>(T component) where T : IDebuggableComponent {
        if(component is Midi midi) {
            VisitExternalMidiDevice(midi);
        }
    }
    
    private void VisitExternalMidiDevice(Midi externalMidiDevice) {
        Midi.LastPortRead = externalMidiDevice.LastPortRead;
        Midi.LastPortWritten = externalMidiDevice.LastPortWritten;
        Midi.LastPortWrittenValue = externalMidiDevice.LastPortWrittenValue;
    }
}