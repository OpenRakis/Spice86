namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Shared.Emulator.Video;

public partial class PaletteViewModel : ViewModelBase {
    private readonly ArgbPalette? _argbPalette;
    private readonly DispatcherTimer _timer;

    public PaletteViewModel() {
        if (!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        _timer = new DispatcherTimer();
    }

    
    public PaletteViewModel(ArgbPalette argbPalette) {
        _argbPalette = argbPalette;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors);
        for (int i = 0; i < 256; i++) {
            _palette.Add(new (){Fill = new SolidColorBrush()});
        }
        _timer.Start();
    }

    [ObservableProperty]
    private AvaloniaList<Rectangle> _palette = new();

    /// <summary>
    /// Invoked by the timer to update the displayed colors.
    /// </summary>
    /// <param name="sender">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void UpdateColors(object? sender, EventArgs e) {
        try {
            if(_argbPalette is null) {
                return;
            }
            for(int i = 0; i < Palette.Count; i++) {
                Rectangle rectangle = Palette[i];
                uint source = _argbPalette[i];
                Rgb rgb = Rgb.FromUint(source);
                if (rectangle.Fill is SolidColorBrush fill) {
                    fill.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                }
            }
        } catch {
            //A read during emulation provoked an OutOfRangeException (for example, in the DAC).
            // Ignore it.
        }
    }
}