namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Spice86.ViewModels;

using System.ComponentModel;

internal partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
    }

    private void InvalidateImage() {
        Image.InvalidateVisual();
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        FocusOnVideoBuffer();
        var mainVm = (MainWindowViewModel?)DataContext;
        Image.PointerMoved -= (s, e) => mainVm?.OnMouseMoved(e, Image);
        Image.PointerPressed -= (s, e) => mainVm?.OnMouseButtonDown(e, Image);
        Image.PointerReleased -= (s, e) => mainVm?.OnMouseButtonUp(e, Image);
        Image.PointerMoved += (s, e) => mainVm?.OnMouseMoved(e, Image);
        Image.PointerPressed += (s, e) => mainVm?.OnMouseButtonDown(e, Image);
        Image.PointerReleased += (s, e) => mainVm?.OnMouseButtonUp(e, Image);
        mainVm?.OnMainWindowInitialized(this.InvalidateImage);
    }

    protected override void OnClosed(EventArgs e) {
        (DataContext as MainWindowViewModel)?.Dispose();
        base.OnClosed(e);
    }

    private void FocusOnVideoBuffer() {
        Image.IsEnabled = false;
        Image.Focus();
        Image.IsEnabled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyUp(e);
        FocusOnVideoBuffer();
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        FocusOnVideoBuffer();
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        AppClosing?.Invoke(this, e);
        base.OnClosing(e);
    }

    public static event EventHandler<CancelEventArgs>? AppClosing;
}