namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;

public partial class PaletteViewModel : ObservableObject {
    private readonly Machine? _machine;
    private DispatcherTimer _timer;

    public PaletteViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        _timer = new DispatcherTimer();
    }

    
    public PaletteViewModel(Machine? machine) {
        _machine = machine;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors);
        _timer.Start();
        for (int i = 0; i < 256; i++) {
            _palette.Add(new (){Fill = new SolidColorBrush()});
        }
    }

    [ObservableProperty]
    private AvaloniaList<Rectangle> _palette = new();

    /// <summary>
    /// Invoked by the timer to update the displayed colors.
    /// </summary>
    /// <param name="sender">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void UpdateColors(object? sender, EventArgs e) {
        AeonCard? videoCard = _machine?.VgaCard as AeonCard;
        if (videoCard is null) {
            return;
        }
        ReadOnlySpan<Rgb> palette = videoCard.Dac.Palette;
        for(int i = 0; i < Palette.Count; i++) {
            Rectangle rectangle = Palette[i];
            Rgb source = palette[i];
            if (rectangle.Fill is SolidColorBrush fill &&
                (source.R != fill.Color.R || source.G != fill.Color.G || source.B != fill.Color.B)) {
                fill.Color = Color.FromRgb(source.R, source.G, source.B);
            }
        }
    }
}