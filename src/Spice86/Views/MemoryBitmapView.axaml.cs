namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using AvaloniaHex;

using Spice86.ViewModels;
using Spice86.ViewModels.Services;

/// <summary>
///     Code-behind for the MemoryBitmapView, managing the DispatcherTimer lifecycle
///     for periodic bitmap refresh, the embedded HexEditor selection wiring,
///     and pointer tracking for pixel hover info on the bitmap image.
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
            Image? bitmapImage = this.FindControl<Image>("BitmapImage");
            if (bitmapImage is not null) {
                bitmapImage.PointerMoved -= OnBitmapPointerMoved;
                bitmapImage.PointerMoved += OnBitmapPointerMoved;
                bitmapImage.PointerExited -= OnBitmapPointerExited;
                bitmapImage.PointerExited += OnBitmapPointerExited;
            }
        }
    }

    private void OnBitmapPointerMoved(object? sender, PointerEventArgs e) {
        if (DataContext is not MemoryBitmapViewModel vm || sender is not Image image) {
            return;
        }
        if (vm.RenderedBitmap is null) {
            return;
        }
        Point position = e.GetPosition(image);
        int imageWidth = vm.RenderedBitmap.PixelSize.Width;
        int imageHeight = vm.RenderedBitmap.PixelSize.Height;
        double scaleX = image.Bounds.Width > 0 ? imageWidth / image.Bounds.Width : 1;
        double scaleY = image.Bounds.Height > 0 ? imageHeight / image.Bounds.Height : 1;
        int pixelX = (int)(position.X * scaleX);
        int pixelY = (int)(position.Y * scaleY);
        vm.UpdateHoverInfo(pixelX, pixelY);
    }

    private void OnBitmapPointerExited(object? sender, PointerEventArgs e) {
        if (DataContext is MemoryBitmapViewModel vm) {
            vm.UpdateHoverInfo(-1, -1);
        }
    }
}
