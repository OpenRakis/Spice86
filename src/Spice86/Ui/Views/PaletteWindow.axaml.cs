namespace Spice86.Ui.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

public partial class PaletteWindow : Window {
    private DispatcherTimer? timer;
    private MainWindowViewModel? _mainWindowViewModel;
    private UniformGrid? grid;

    public PaletteWindow() {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        this.grid = this.FindControl<UniformGrid>("grid");
    }

    public PaletteWindow(Window mainWindow, MainWindowViewModel mainWindowViewModel) {
        InitializeComponent();
        this.Owner = mainWindow;
        _mainWindowViewModel = mainWindowViewModel;
    }

    protected override void OnOpened(EventArgs e) {
        if (this.grid is null) {
            return;
        }
        base.OnOpened(e);
        for (int i = 0; i < 256; i++)
            this.grid.Children.Add(new Rectangle() { Fill = new SolidColorBrush() });

        this.timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors);
        this.timer.Start();
    }

    /// <summary>
    /// Invoked by the timer to update the displayed colors.
    /// </summary>
    /// <param name="sender">Source of the event.</param>
    /// <param name="e">Unused EventArgs instance.</param>
    private void UpdateColors(object? sender, EventArgs e) {
        Emulator.Devices.Video.Rgb[]? palette = _mainWindowViewModel?.Palette;
        if (this.grid is null) {
            return;
        }
        if (palette is null)
            return;

        for (int i = 0; i < palette.Length; i++) {
            Emulator.Devices.Video.Rgb? rgb = palette[i];
            if (rgb is null) {
                continue;
            }
            var brush = (SolidColorBrush?)((Rectangle)this.grid.Children[i]).Fill;
            if (brush is null) {
                continue;
            }
            brush.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
    }
}
