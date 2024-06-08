namespace Spice86.ViewModels;

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InternalDebugger;
using Spice86.Infrastructure;
using Spice86.Shared.Emulator.Video;

public partial class PaletteViewModel : ViewModelBase, IInternalDebugger {
    private ArgbPalette? _argbPalette;
    
    public PaletteViewModel() {
        if(!Design.IsDesignMode) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
    }
    
    public PaletteViewModel(IUIDispatcherTimerFactory dispatcherTimerFactory) {
        dispatcherTimerFactory.StartNew(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, UpdateValues);
        Dispatcher.UIThread.Post(() => {
            for (int i = 0; i < 256; i++) {
                _palette.Add(new (){Fill = new SolidColorBrush()});
            }
        });
    }

    private void UpdateValues(object? sender, EventArgs e) {
        if (_argbPalette is null) {
            return;
        }
        UpdateColors(_argbPalette);
    }

    [ObservableProperty]
    private AvaloniaList<Rectangle> _palette = new();

    private void UpdateColors(ArgbPalette palette) {
        try {
            for(int i = 0; i < Palette.Count; i++) {
                Rectangle rectangle = Palette[i];
                uint source = palette[i];
                Rgb rgb = Rgb.FromUint(source);
                if (rectangle.Fill is SolidColorBrush fill) {
                    fill.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                }
            }
        } catch(IndexOutOfRangeException) {
            //A read during emulation provoked an OutOfRangeException (for example, in the DAC).
            // Ignore it.
        }
    }

    public bool NeedsToVisitEmulator => _argbPalette is null;

    public void Visit<T>(T component) where T : IDebuggableComponent {
        _argbPalette ??= component as ArgbPalette;
    }
}