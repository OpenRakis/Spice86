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
        this.Menu.KeyDown += OnMenuKeyDown;
        this.Menu.KeyDown += OnMenuKeyUp;
        this.Menu.GotFocus += OnMenuGotFocus;
    }

    public PerformanceViewModel? PerformanceViewModel { get; init; }

    private void OnMenuGotFocus(object? sender, GotFocusEventArgs e) {
        FocusOnVideoBuffer();
        e.Handled = true;
    }

    private void OnMenuKeyUp(object? sender, KeyEventArgs e) {
          (DataContext as MainWindowViewModel)?.OnKeyUp(e);
          e.Handled = true;
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        e.Handled = true;
    }

    private void FocusOnVideoBuffer() {
        Image.Focus();
    }

    protected override void OnOpened(EventArgs e) {
        base.OnOpened(e);
        if (DataContext is not MainWindowViewModel mainVm) {
            return;
        }
        mainVm.CloseMainWindow += (_, _) => Close();
        mainVm.InvalidateBitmap += Image.InvalidateVisual;
        Image.PointerMoved += (s, e) => mainVm.OnMouseMoved(e, Image);
        Image.PointerPressed += (s, e) => mainVm.OnMouseButtonDown(e, Image);
        Image.PointerReleased += (s, e) => mainVm.OnMouseButtonUp(e, Image);
        FocusOnVideoBuffer();
        mainVm.StartEmulator();
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.OnKeyUp(e);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }
}