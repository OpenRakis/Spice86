namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Spice86.ViewModels;

using System;

internal partial class VideoBufferView : UserControl {
    public VideoBufferView() {
        InitializeComponent();
        DataContextChanged += VideoBufferView_DataContextChanged;
        MainWindow.AppClosing += MainWindow_AppClosing;
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }
    private bool _appClosing;

    private void VideoBufferView_DataContextChanged(object? sender, EventArgs @event) {
        if (DataContext is not VideoBufferViewModel vm ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            return;
        }

        if (Image is not null && desktop.MainWindow is MainWindow mainWindow && desktop.MainWindow.DataContext is MainWindowViewModel mainVm) {
            mainWindow.SetPrimaryDisplayControl(Image);
            Image.PointerMoved -= (s, e) => mainVm.OnMouseMoved(e, Image);
            Image.PointerPressed -= (s, e) => mainVm.OnMouseButtonDown(e, Image);
            Image.PointerReleased -= (s, e) => mainVm.OnMouseButtonUp(e, Image);
            Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, Image);
            Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, Image);
            Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, Image);
        }
        vm.SetUIUpdateMethod(InvalidateImage);
    }

    private void InvalidateImage() {
        if (_appClosing || Image is null) {
            return;
        }
        Image.InvalidateVisual();
    }
}
