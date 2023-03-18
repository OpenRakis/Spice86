namespace Spice86.ViewModels;

using Avalonia.Controls;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;

public partial class DebugViewModel : ObservableObject {
    [ObservableProperty]
    private Machine? _machine;
    
    [ObservableProperty]
    private AeonCard? _videoCard;

    private readonly DispatcherTimer _timer;
    
    public DebugViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        _timer = new DispatcherTimer();
    }
    
    public DebugViewModel(Machine? machine) {
        _machine = machine;
        _videoCard = _machine?.VgaCard as AeonCard;
        _timer = new(TimeSpan.FromMilliseconds(300), DispatcherPriority.Normal, UpdateValues);
        _timer.Start();
    }

    private void UpdateValues(object? sender, EventArgs e) {
        OnPropertyChanged(nameof(this.Machine));
        OnPropertyChanged(nameof(this.VideoCard));
    }
}