namespace Spice86.Models.Debugging;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class SoundChannelInfo : ObservableObject {
    [ObservableProperty] private int _volume;
    [ObservableProperty] private float _stereoSeparation;

    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private string _name = "";
}