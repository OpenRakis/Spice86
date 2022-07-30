namespace Spice86.AvaloniaUI.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

using Spice86.AvaloniaUI.ViewModels;
using Spice86.Shared;

using System.Collections.ObjectModel;

internal partial class PaletteWindow : Window {
    private DispatcherTimer? _timer;
    private readonly MainWindowViewModel? _mainWindowViewModel;
    private UniformGrid? _grid;

    public PaletteWindow() {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        _grid = this.FindControl<UniformGrid>("grid");
    }

    public PaletteWindow(MainWindowViewModel mainWindowViewModel) {
        InitializeComponent();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            Owner = desktop.MainWindow;
        }
        _mainWindowViewModel = mainWindowViewModel;
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        if (_grid is null) {
            return;
        }
        for (int i = 0; i < 256; i++)
            _grid.Children.Add(new Rectangle() { Fill = new SolidColorBrush() });

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors);
        _timer.Start();
    }

    /// <summary>
    /// Invoked by the timer to update the displayed colors.
    /// </summary>
    /// <param name="sender">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void UpdateColors(object? sender, EventArgs e) {
        ReadOnlyCollection<Rgb>? palette = _mainWindowViewModel?.Palette;
        if (_grid is null) {
            return;
        }
        if (palette is null)
            return;

        for (int i = 0; i < palette.Count; i++) {
            Rgb? rgb = palette[i];
            if (rgb is null) {
                continue;
            }
            var brush = (SolidColorBrush?)((Rectangle)_grid.Children[i]).Fill;
            if (brush is null) {
                continue;
            }
            brush.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
    }
}
