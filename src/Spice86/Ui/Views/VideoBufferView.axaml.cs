namespace Spice86.UI.Views;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Spice86.UI.ViewModels;

using System;
using System.Linq;
using System.Threading.Tasks;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();

    }

    private MainWindow? ApplicationWindow => this.GetSelfAndLogicalAncestors().OfType<MainWindow>().FirstOrDefault();

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        Initialized += VideoBufferView_Initialized;
        MainWindow.AppClosing += MainWindow_AppClosing;
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }
    private Image? _image;
    private bool _appClosing;

    private void VideoBufferView_Initialized(object? sender, EventArgs e) {
        if (this.DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            if (vm.IsPrimaryDisplay && _image is not null && ApplicationWindow?.DataContext is MainWindowViewModel mainVm) {
                _image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => mainVm.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => mainVm.OnMouseClick(e, false);
            }
            vm.SetUIUpdateMethod(InvalidateImageAsync);
        }
    }

    private void InvalidateImageAsync() {
        if (this.DataContext is VideoBufferViewModel vm && _appClosing == false) {
            if (_image is null) {
                return;
            }
            if (_image.Source is null) {
                _image.Source = vm.Bitmap;
            }
            _image.InvalidateVisual();
        }
    }
}
