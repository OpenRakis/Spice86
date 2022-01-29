namespace Spice86.UI.Views;

using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Initialized += VideoBufferView_Initialized;
    }

    private Image? _image;

    private void VideoBufferView_Initialized(object? sender, EventArgs e) {
        if(this.DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            InitializeBitmap(vm);
            if (vm.IsPrimaryDisplay) {
                _image.PointerMoved += (s, e) => vm.MainWindowViewModel?.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, false);
                _image.KeyDown += (s, e) => vm.MainWindowViewModel?.OnKeyPressed(e);
                _image.KeyUp += (s, e) => vm.MainWindowViewModel?.OnKeyReleased(e);
            }
            vm.Dirty += VideoBufferViewModel_IsDirty;
        }
    }

    /// <summary>
    /// We initialize the ViewModel WriteableBitmap here because only the View can get the DPI.
    /// </summary>
    /// <param name="vm">The current view <see cref="VideoBufferViewModel"/></param>
    private void InitializeBitmap(VideoBufferViewModel vm) {
        //var window = this.VisualRoot as Window;
        //var currentDpi = window?.PlatformImpl.DesktopScaling;
        // TODO : Get current DPI from Avalonia or Skia.
        // It isn't DesktopScaling or RenderScaling as this returns 1 when Windows Desktop Scaling is set at 100%
        // TODO: Find what is the inherithed event fired for when DPI changes, and react to it.
        var dpi = 75d;
        var bitmap = new WriteableBitmap(new PixelSize(vm.Width, vm.Height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        vm.Bitmap = bitmap;
    }

    private async void VideoBufferViewModel_IsDirty(object? sender, EventArgs e) {
        if(_image is null) {
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() => {
            _image.InvalidateVisual();
        }, DispatcherPriority.MaxValue);
    }
}
