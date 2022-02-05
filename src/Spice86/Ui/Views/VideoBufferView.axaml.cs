namespace Spice86.UI.Views;

using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
        this.AttachedToVisualTree += VideoBufferView_AttachedToVisualTree;

    }

    private MainWindow? ApplicationWindow => this.GetSelfAndLogicalAncestors().OfType<MainWindow>().FirstOrDefault();

    private void VideoBufferView_AttachedToVisualTree(object? sender, EventArgs e) {
        if (ApplicationWindow is MainWindow) {
            ApplicationWindow.KeyUp += MainWindow_KeyUp;
            ApplicationWindow.KeyDown += MainWindow_KeyDown;
        }
    }

    private void MainWindow_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) {
        if (this.DataContext is VideoBufferViewModel vm) {
            vm.MainWindowViewModel?.OnKeyPressed(e);
        }
    }

    private void MainWindow_KeyUp(object? sender, Avalonia.Input.KeyEventArgs e) {
        if (this.DataContext is VideoBufferViewModel vm) {
            vm.MainWindowViewModel?.OnKeyReleased(e);
        }
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Initialized += VideoBufferView_Initialized;
    }

    private Image? _image;

    private void VideoBufferView_Initialized(object? sender, EventArgs e) {
        if (this.DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            InitializeBitmap(vm);
            if (vm.IsPrimaryDisplay && _image is not null) {
                _image.PointerMoved += (s, e) => vm.MainWindowViewModel?.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, false);
            }
            vm.Dirty += VideoBufferViewModel_IsDirty;
        }
    }

    /// <summary>
    /// We initialize the ViewModel WriteableBitmap here because only the View can get the DPI.
    /// </summary>
    /// <param name="vm">The current view <see cref="VideoBufferViewModel"/></param>
    private static void InitializeBitmap(VideoBufferViewModel vm) {
        //var window = this.VisualRoot as Window;
        //var currentDpi = window?.PlatformImpl.DesktopScaling;
        // TODO : Get current DPI from Avalonia or Skia.
        // It isn't DesktopScaling or RenderScaling as this returns 1 when Windows Desktop Scaling is set at 100%
        // TODO: Find what is the (inherithed ?) event fired for when DPI changes, and react to it.
        var dpi = 75d;
        var bitmapSize = new PixelSize(vm.Width, vm.Height);
        var bitmapDpi = new Vector(dpi, dpi);
        if (vm.Bitmap.Size.Width != bitmapSize.Width || vm.Bitmap.Size.Height != bitmapSize.Height || vm.Bitmap.Dpi != bitmapDpi) {
            vm.Bitmap.Dispose();
            var bitmap = new WriteableBitmap(bitmapSize, bitmapDpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            vm.Bitmap = bitmap;
        }
    }

    private async void VideoBufferViewModel_IsDirty(object? sender, EventArgs e) {
        await Dispatcher.UIThread.InvokeAsync(() => {
            if (this.DataContext is VideoBufferViewModel vm) {
                if (_image is null) {
                    return;
                }
                if (_image.Source is null) {
                    _image.Source = vm.Bitmap;
                }
                _image.InvalidateVisual();
            }

        }, DispatcherPriority.MaxValue);
    }
}
