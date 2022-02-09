namespace Spice86.UI.Views;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

using System;
using System.Linq;

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
            if (vm.IsPrimaryDisplay && _image is not null) {
                _image.PointerMoved += (s, e) => vm.MainWindowViewModel?.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => vm.MainWindowViewModel?.OnMouseClick(e, false);
            }
            vm.Dirty += VideoBufferViewModel_IsDirty;
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
