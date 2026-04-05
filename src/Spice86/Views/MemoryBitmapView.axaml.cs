namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using AvaloniaHex;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

/// <summary>
///     Code-behind for the MemoryBitmapView, managing the DispatcherTimer lifecycle
///     for periodic bitmap refresh and wiring the embedded HexEditor selection.
/// </summary>
public partial class MemoryBitmapView : UserControl {
    private DispatcherTimer? _timer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryBitmapView"/> class.
    /// </summary>
    public MemoryBitmapView() {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        if (DataContext is MemoryBitmapViewModel vm) {
            vm.IsVisible = false;
            _timer?.Stop();
            _timer = null;
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is MemoryBitmapViewModel vm) {
            vm.IsVisible = this.IsVisible;
            _timer = DispatcherTimerStarter.StartNewDispatcherTimer(
                TimeSpan.FromMilliseconds(500),
                DispatcherPriority.Background,
                vm.UpdateValues);
            HexEditor? hexEditor = this.FindControl<HexEditor>("HexViewer");
            if (hexEditor is not null) {
                hexEditor.Selection.RangeChanged -= vm.OnHexSelectionRangeChanged;
                hexEditor.Selection.RangeChanged += vm.OnHexSelectionRangeChanged;
            }
        }
    }
}
