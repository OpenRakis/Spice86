namespace Spice86.Views;

using Avalonia.Controls;
using Avalonia.Input;

using Spice86.ViewModels;

using System;

internal partial class MainWindow : Window {
    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public MainWindow() {
        InitializeComponent();
    }

    private void FocusOnVideoBuffer() {
        Image.IsEnabled = false;
        Image.Focus();
        Image.IsEnabled = true;
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        if (DataContext is not MainWindowViewModel mainVm) {
            return;
        }
        mainVm.CloseMainWindow += (_, _) => Close();
        mainVm.OnMainWindowInitialized(Image.InvalidateVisual);
        Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, Image);
        Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, Image);
        Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, Image);
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyUp(e);
        if (this.Image.IsFocused) {
            e.Handled = true;
        } else {
            FocusOnVideoBuffer();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        if (this.Image.IsFocused) {
            e.Handled = true;
        } else {
            FocusOnVideoBuffer();
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }
}