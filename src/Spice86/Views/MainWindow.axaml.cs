namespace Spice86.Views;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.Native;
using Spice86.ViewModels;

using System;

internal partial class MainWindow : Window {
    private bool _isMouseCaptured = false;
    private bool _wantToCapture = true;
    private IntPtr _windowHandle;

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        this.Menu.KeyDown += OnMenuKeyDown;
        this.Menu.KeyDown += OnMenuKeyUp;
        this.Menu.GotFocus += OnMenuGotFocus;
        this.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        // Get native window handle when window is loaded
        _windowHandle = GetNativeWindowHandle();

        Dispatcher.UIThread.Post(() => {
            if (DataContext is MainWindowViewModel mainVm) {
                mainVm.CloseMainWindow += (_, _) => Close();
                mainVm.InvalidateBitmap += Image.InvalidateVisual;
                Image.PointerMoved += OnMouseMoved;
                Image.PointerPressed += OnPointerPressed;
                Image.PointerReleased += OnMouseButtonUp;
                mainVm.StartEmulator();
            }
        }, DispatcherPriority.Background);
    }

    // Get the native window handle
    private IntPtr GetNativeWindowHandle() {
        return TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    public static readonly StyledProperty<PerformanceViewModel?> PerformanceViewModelProperty =
        AvaloniaProperty.Register<MainWindow, PerformanceViewModel?>(nameof(PerformanceViewModel),
            defaultValue: null);

    public PerformanceViewModel? PerformanceViewModel {
        get => GetValue(PerformanceViewModelProperty);
        set => SetValue(PerformanceViewModelProperty, value);
    }

    private void OnMouseMoved(object? sender, PointerEventArgs e) {
        if (this.DataContext is MainWindowViewModel mainVm) {
            if (!_isMouseCaptured && _wantToCapture) {
                _wantToCapture = false;
                _isMouseCaptured = true;

                // Get the bounds of the ScreenView control
                Rect screenViewBounds = ScreenView.Bounds;
                Point screenViewPoint = ScreenView.PointToClient(new(0, 0));

                // Use the new method with the ScreenView bounds
                NativeMouseCapture.EnableCaptureWithBounds(
                    _windowHandle,
                    (int)screenViewPoint.X,
                    (int)screenViewPoint.Y,
                    (int)(screenViewPoint.X + screenViewBounds.Width),
                    (int)(screenViewPoint.Y + screenViewBounds.Height)
                );
            }
            mainVm.OnMouseMoved(e, Image);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (this.DataContext is not MainWindowViewModel mainVm) {
            return;
        }

        if (e.GetCurrentPoint(Image).Properties.IsMiddleButtonPressed) {
            _isMouseCaptured = !_isMouseCaptured;
            _wantToCapture = !_wantToCapture;

            if (_isMouseCaptured) {
                // Get the bounds of the ScreenView control
                Rect screenViewBounds = ScreenView.Bounds;
                Point screenViewPoint = ScreenView.PointToClient(new(0, 0));

                // Use the new method with the ScreenView bounds
                NativeMouseCapture.EnableCaptureWithBounds(
                    _windowHandle,
                    (int)screenViewPoint.X,
                    (int)screenViewPoint.Y,
                    (int)(screenViewPoint.X + screenViewBounds.Width),
                    (int)(screenViewPoint.Y + screenViewBounds.Height)
                );
            } else {
                NativeMouseCapture.DisableCapture();
            }
            // We've handled the middle-click, so we can stop here.
            e.Handled = true;
            return;
        }

        mainVm.OnMouseButtonDown(e, Image);
    }

    private void OnMouseButtonUp(object? sender, PointerReleasedEventArgs e) {
        if (this.DataContext is MainWindowViewModel mainVm) {
            // The middle mouse button is only for toggling capture, not for the emulator.
            if (e.InitialPressMouseButton == MouseButton.Middle) {
                e.Handled = true;
                return;
            }
            mainVm.OnMouseButtonUp(e, Image);
        }
    }

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

    protected override void OnKeyUp(KeyEventArgs e) {
        FocusOnVideoBuffer();
        var mainWindowViewModel = (DataContext as MainWindowViewModel);
        mainWindowViewModel?.OnKeyUp(e);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.OnKeyDown(e);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        // Release mouse capture when closing
        NativeMouseCapture.Cleanup();

        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}