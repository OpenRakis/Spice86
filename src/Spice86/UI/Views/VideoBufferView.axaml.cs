namespace Spice86.UI.Views;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using Spice86.UI.ViewModels;

using System;
using System.Linq;

public partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
    }

    private MainWindow? ApplicationWindow => this.GetSelfAndLogicalAncestors().OfType<MainWindow>().FirstOrDefault();

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
        if (this.DataContext is VideoBufferViewModel vm) {
            _image = this.FindControl<Image>(nameof(Image));
            if (vm._isPrimaryDisplay && _image is not null && ApplicationWindow?.DataContext is MainWindowViewModel mainVm) {
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
