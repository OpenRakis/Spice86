namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Sound.Midi;
using Spice86.ViewModels.PropertiesMappers;
using Spice86.ViewModels.ValueViewModels.Debugging;

public partial class MidiViewModel : TimerRefreshViewModelBase {
    public override string Header => "General MIDI / MT-32";
    [ObservableProperty]
    private MidiInfo _midi = new();

    private readonly Midi _externalMidiDevice;

    public MidiViewModel(Midi externalMidiDevice) : base(400) {
        _externalMidiDevice = externalMidiDevice;
    }

    protected override void RefreshCore() {
        _externalMidiDevice.CopyToMidiInfo(Midi);
    }
}