namespace Spice86.WPF.Views;

using Spice86.Shared;
using Spice86.UI.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

/// <summary>
/// Interaction logic for PaletteWindow.xaml
/// </summary>
public partial class PaletteWindow : Window {
    private readonly WPFMainWindowViewModel? _mainWindowViewModel;
    private Grid? _grid;
    private DispatcherTimer? _timer;

    public PaletteWindow() {
        InitializeComponent();
    }

    public PaletteWindow(WPFMainWindowViewModel mainWindowViewModel) {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        _mainWindowViewModel = mainWindowViewModel;
    }

    protected override void OnInitialized(EventArgs e) {
        base.OnInitialized(e);
        if (_grid is null) {
            return;
        }
        for (int i = 0; i < 256; i++)
            _grid.Children.Add(new Rectangle() { Fill = new SolidColorBrush() });

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Normal, UpdateColors, Dispatcher);
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
