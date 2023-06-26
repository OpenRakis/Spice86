namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
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
    private MainWindowViewModel? _mainVm;

    protected override void OnKeyUp(KeyEventArgs e) {
        _mainVm?.OnKeyUp(e);
        base.OnKeyUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        _mainVm?.OnKeyDown(e);
        base.OnKeyDown(e);
    }

    private void VideoBufferView_DataContextChanged(object? sender, EventArgs @event) {
        if (DataContext is not VideoBufferViewModel vm ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
            return;
        }

        _image = this.FindControl<Image>(nameof(Image));
        if (_image is not null && desktop.MainWindow is MainWindow mainWindow && desktop.MainWindow.DataContext is MainWindowViewModel mainVm) {
            mainWindow.SetPrimaryDisplayControl(_image);
            _mainVm = mainVm;
            _image.PointerMoved += (_, e) => _mainVm?.OnMouseMoved(e, _image);
            _image.PointerPressed += (_, e) => _mainVm?.OnMouseButtonDown(e, _image);
            _image.PointerReleased += (_, e) => _mainVm?.OnMouseButtonUp(e, _image);
        }
        vm.SetUIUpdateMethod(InvalidateImage);
    }

    private void InvalidateImage() {
        if (_appClosing || _image is null) {
            return;
        }
        _image.InvalidateVisual();
    }
}
