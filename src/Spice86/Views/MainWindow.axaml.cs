
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Spice86.Native;
using Spice86.ViewModels;

using System.Runtime.InteropServices;

using AvaloniaMouseButton = Avalonia.Input.MouseButton;

namespace Spice86.Views;

internal partial class MainWindow : Window {
    // When SDL relative mode is active, track absolute cursor position (0..1) by accumulating deltas.
    // This matches the emulator's virtual screen width (640 px) so one pixel equals one virtual pixel.
    private const double VirtualScreenWidth = 640.0;

    private bool _isCaptured;
    private IMouseCaptureBackend? _captureBackend;
    private double _sdlCapturedMouseX = 0.5;
    private double _sdlCapturedMouseY = 0.5;
    private DispatcherTimer? _relativePollTimer;
    private bool _windowClosed;

    public void ShowMouseCursor() {
        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.Display.ShowMouseCursor();
        }
    }

    public void HideMouseCursor() {
        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.Display.HideMouseCursor();
        }
    }

    /// <summary>
    /// Initializes a new instance
    /// </summary>
    public MainWindow() {
        InitializeComponent();
        this.Menu.KeyDown += OnMenuKeyDown;
        this.Menu.KeyDown += OnMenuKeyUp;
        this.Menu.GotFocus += OnMenuGotFocus;
        this.CyclesLimitingNumericUpDown.GotFocus += CyclesLimitingNumericUpDown_GotFocus;
        this.TimeMultiplierNumericUpDown.GotFocus += CyclesLimitingNumericUpDown_GotFocus;
        this.Loaded += MainWindow_Loaded;
        Deactivated += OnWindowDeactivated;
    }

    private void CyclesLimitingNumericUpDown_GotFocus(object? sender, FocusChangedEventArgs e) {
        FocusOnVideoBuffer();
        e.Handled = true;
    }

    private void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
        Dispatcher.UIThread.Post(() => {
            if (DataContext is MainWindowViewModel mainVm) {
                mainVm.Display.InvalidateBitmap += Image.InvalidateVisual;
                Image.PointerMoved += OnMouseMoved;
                Image.PointerPressed += OnPointerPressed;
                Image.PointerReleased += OnMouseButtonUp;
                InitializeMouseCapture();
                UpdateCaptureUiState();
                FocusOnVideoBuffer();
                _ = StartMainWindowSessionAsync(mainVm);
            }
        }, DispatcherPriority.Background);
    }

    private async Task StartMainWindowSessionAsync(MainWindowViewModel mainVm) {
        bool shouldCloseWindow = await mainVm.StartEmulatorAsync();
        if (shouldCloseWindow && !_windowClosed) {
            Close();
        }
    }

    private void InitializeMouseCapture() {
        nint nativeHandle = GetNativeWindowHandle();
        IMouseCaptureBackend backend = CreateCaptureBackend();
        bool initialized;
        try {
            initialized = backend.TryInitialize(nativeHandle);
        } catch {
            backend.Dispose();
            throw;
        }

        if (!initialized) {
            backend.Dispose();
            return;
        }

        _captureBackend = backend;

        // On Windows, re-apply the ClipCursor rect whenever the window moves or resizes.
        if (!backend.UsesRelativeMouseMode) {
            PositionChanged += OnWindowPositionChanged;
            SizeChanged += OnWindowSizeChanged;
        }
    }

    private static IMouseCaptureBackend CreateCaptureBackend() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return new WindowsMouseCaptureBackend();
        }

        return new SdlMouseCapture();
    }

    private nint GetNativeWindowHandle() {
        Avalonia.Platform.IPlatformHandle? platformHandle = TryGetPlatformHandle();
        if (platformHandle is null) {
            return nint.Zero;
        }

        return platformHandle.Handle;
    }

    private void OnMouseMoved(object? sender, PointerEventArgs e) {
        // When SDL relative mode is active, Avalonia pointer events are not useful;
        // deltas are fed by the polling timer instead.
        if (_isCaptured && _captureBackend is { UsesRelativeMouseMode: true }) {
            return;
        }

        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.Display.OnMouseMoved(e, Image);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (e.GetCurrentPoint(Image).Properties.IsMiddleButtonPressed) {
            // Toggle happens on release; consume the press here.
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.Display.OnMouseButtonDown(e, Image);
        }
    }

    private void OnMouseButtonUp(object? sender, PointerReleasedEventArgs e) {
        if (e.InitialPressMouseButton == AvaloniaMouseButton.Middle) {
            ToggleMouseCapture();
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.Display.OnMouseButtonUp(e, Image);
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) {
        ReleaseCapture();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) {
        ReleaseCapture();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) {
        if (_isCaptured && _captureBackend is not null) {
            _captureBackend.EnableCapture();
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) {
        if (_isCaptured && _captureBackend is not null) {
            _captureBackend.EnableCapture();
        }
    }

    private void ToggleMouseCapture() {
        if (_isCaptured) {
            ReleaseCapture();
        } else {
            EnableCapture();
        }
    }

    private void EnableCapture() {
        if (!Image.IsVisible || _captureBackend is null) {
            return;
        }

        if (_captureBackend.EnableCapture()) {
            if (_captureBackend.UsesRelativeMouseMode) {
                _sdlCapturedMouseX = 0.5;
                _sdlCapturedMouseY = 0.5;
                StartRelativePollTimer();
            }

            _isCaptured = true;
            UpdateCaptureUiState();
        }
    }

    private void ReleaseCapture() {
        if (_captureBackend is not null) {
            _captureBackend.DisableCapture();
        }

        StopRelativePollTimer();
        _isCaptured = false;
        UpdateCaptureUiState();
    }

    private void StartRelativePollTimer() {
        if (_relativePollTimer is not null) {
            return;
        }

        // Poll at ~100 Hz to match the mouse device sample rate.
        _relativePollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Input, OnRelativePollTick);
        _relativePollTimer.Start();
    }

    private void StopRelativePollTimer() {
        if (_relativePollTimer is null) {
            return;
        }

        _relativePollTimer.Stop();
        _relativePollTimer = null;
    }

    private void OnRelativePollTick(object? sender, EventArgs e) {
        if (_captureBackend is null || !_captureBackend.IsCaptured) {
            return;
        }

        if (DataContext is not MainWindowViewModel mainVm) {
            return;
        }

        _captureBackend.GetRelativeMouseDelta(out int dx, out int dy);
        if (dx == 0 && dy == 0) {
            return;
        }

        // Convert pixel deltas to normalised coordinates.
        // VirtualScreenWidth matches the emulator's virtual screen width (640) so one pixel = one virtual pixel.
        double normalizedDx = dx / VirtualScreenWidth;
        double normalizedDy = dy / VirtualScreenWidth;
        _sdlCapturedMouseX = Math.Clamp(_sdlCapturedMouseX + normalizedDx, 0.0, 1.0);
        _sdlCapturedMouseY = Math.Clamp(_sdlCapturedMouseY + normalizedDy, 0.0, 1.0);

        mainVm.Display.OnMouseMovedNormalized(_sdlCapturedMouseX, _sdlCapturedMouseY);
    }

    private void UpdateCaptureUiState() {
        TopBar.IsVisible = !_isCaptured;
        BottomBar.IsVisible = !_isCaptured;
        if (DataContext is MainWindowViewModel mainVm) {
            mainVm.UpdateMouseCaptureHint(_isCaptured);
        }
    }

    private void OnMenuGotFocus(object? sender, FocusChangedEventArgs e) {
        FocusOnVideoBuffer();
        e.Handled = true;
    }

    private void OnMenuKeyUp(object? sender, KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.Display.OnKeyUp(e);
        e.Handled = true;
    }

    private void OnMenuKeyDown(object? sender, KeyEventArgs e) {
        (DataContext as MainWindowViewModel)?.Display.OnKeyDown(e);
        e.Handled = true;
    }

    private void FocusOnVideoBuffer() {
        Image?.Focus();
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.Display.OnKeyUp(e);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        FocusOnVideoBuffer();
        (DataContext as MainWindowViewModel)?.Display.OnKeyDown(e);
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e) {
        ReleaseCapture();
        (DataContext as MainWindowViewModel)?.OnMainWindowClosing();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        _windowClosed = true;
        Deactivated -= OnWindowDeactivated;
        if (_captureBackend is { UsesRelativeMouseMode: false }) {
            PositionChanged -= OnWindowPositionChanged;
            SizeChanged -= OnWindowSizeChanged;
        }

        _captureBackend?.Dispose();
        _captureBackend = null;
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}