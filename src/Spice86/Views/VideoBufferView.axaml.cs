namespace Spice86.Views;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Spice86.ViewModels;

using System;

internal partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += VideoBufferView_DataContextChanged;
        MainWindow.AppClosing += MainWindow_AppClosing;
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }
    private Image? _image;
    private bool _appClosing;

    private void VideoBufferView_DataContextChanged(object? sender, EventArgs e) {
        if (DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            if (vm.IsPrimaryDisplay && _image is not null && App.MainWindow?.DataContext is MainWindowViewModel mainVm) {
                App.MainWindow.SetPrimaryDisplayControl(_image);
                _image.PointerMoved -= (s, e) => mainVm.OnMouseMoved(e, _image);
                _image.PointerPressed -= (s, e) => mainVm.OnMouseClick(e, true);
                _image.PointerReleased -= (s, e) => mainVm.OnMouseClick(e, false);
                _image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, _image);
                _image.PointerPressed += (s, e) => mainVm.OnMouseClick(e, true);
                _image.PointerReleased += (s, e) => mainVm.OnMouseClick(e, false);
            }
            vm.SetUIUpdateMethod(InvalidateImage);
        }
    }

    private void InvalidateImage() {
        if (_appClosing || _image is null) {
            return;
        }
        _image.InvalidateVisual();
    }
}
