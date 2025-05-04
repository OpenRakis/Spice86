namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Infrastructure;
using Spice86.Shared.Emulator.Video;

public partial class PaletteViewModel : ViewModelBase, IEmulatorObjectViewModel {
    private readonly ArgbPalette _argbPalette;

    private readonly Dictionary<uint, Color> ColorsCache = new();
    public PaletteViewModel(ArgbPalette argbPalette, IUIDispatcher uiDispatcher) {
        _argbPalette = argbPalette;
        DispatcherTimerStarter.StartNewDispatcherTimer(TimeSpan.FromMilliseconds(1000), DispatcherPriority.Background, UpdateValues);
    }

    public bool IsVisible { get; set; }


    public void UpdateValues(object? sender, EventArgs e) {
        if (!IsVisible) {
            return;
        }
        UpdateColors(_argbPalette);
    }

    [ObservableProperty]
    private AvaloniaList<Rectangle> _palette = new();

    private void UpdateColors(ArgbPalette palette) {
        if(Palette.Count == 0) {
            for (int i = 0; i < 256; i++) {
                Palette.Add(new() { Fill = new SolidColorBrush() });
            }
        }

        try {
            for (int i = 0; i < Palette.Count; i++) {
                Rectangle rectangle = Palette[i];
                uint source = palette[i];
                SolidColorBrush? brush = (SolidColorBrush?)rectangle.Fill;
                if (ColorsCache.TryGetValue(source, out Color cachedColor)) {
                    if (cachedColor != brush?.Color) {
                        brush!.Color = cachedColor;
                    }
                } else {
                    Rgb rgb = Rgb.FromUint(source);
                    Color color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                    ColorsCache.Add(source, color);
                    if(brush?.Color != color) {
                        brush!.Color = color;
                    }
                }
            }
        } catch (IndexOutOfRangeException) {
            //A read during emulation provoked an OutOfRangeException (for example, in the DAC).
            // Ignore it.
        }
    }
}