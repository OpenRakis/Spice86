namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.Logging;

using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.InterruptHandlers.VGA;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;
using Spice86.ViewModels.Services;

using MouseButton = Spice86.Shared.Emulator.Mouse.MouseButton;

/// <summary>
/// View model that owns the emulator's display surface, input dispatch, and mouse cursor presentation.
/// Implements the GUI-facing interfaces consumed by VGA, mouse, and keyboard subsystems so that
/// <see cref="MainWindowViewModel"/> does not have to be constructed before the program executor.
/// </summary>
public sealed partial class EmulatorDisplayViewModel : ObservableObject,
    IGuiVideoPresentation, IGuiMouseEvents, IGuiKeyboardEvents, IDisposable {
    private const double ScreenRefreshHz = 60;

    private readonly IUIDispatcher _uiDispatcher;
    private readonly IPauseHandler _pauseHandler;
    private readonly SharedMouseData _sharedMouseData;
    private readonly ILoggerService _loggerService;
    private DispatcherTimer? _drawTimer;
    private bool _isSettingResolution;
    private bool _disposed;
    private bool _isAppClosing;

    public EmulatorDisplayViewModel(
        IUIDispatcher uiDispatcher,
        IPauseHandler pauseHandler,
        SharedMouseData sharedMouseData,
        ILoggerService loggerService) {
        _uiDispatcher = uiDispatcher;
        _pauseHandler = pauseHandler;
        _sharedMouseData = sharedMouseData;
        _loggerService = loggerService;
        _drawTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / ScreenRefreshHz),
            DispatcherPriority.Render, (_, _) => DrawScreen());
        _drawTimer.Start();
    }

    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;

    /// <summary>Fires on the UI thread after each frame is rendered into <see cref="Bitmap"/>.</summary>
    internal event Action? InvalidateBitmap;

    [ObservableProperty] private WriteableBitmap? _bitmap;
    [ObservableProperty] private Cursor? _cursor = Cursor.Default;
    [ObservableProperty] private double _aspectRatioCorrectionFactor = 1.0;
    [ObservableProperty] private string _emulatorMouseCursorInfo = "?";

    private bool _showCursor;

    public bool ShowCursor {
        get => _showCursor;
        set {
            SetProperty(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            } else {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double MouseX { get; set; }
    public double MouseY { get; set; }

    public void UpdateResolution(int videoWidth, int videoHeight) {
        _uiDispatcher.Post(() => {
            _isSettingResolution = true;
            if (Width != videoWidth || Height != videoHeight) {
                Width = videoWidth;
                Height = videoHeight;
                if (_disposed) {
                    return;
                }

                Bitmap?.Dispose();
                Bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }

            _isSettingResolution = false;
            UpdateShownEmulatorMouseCursorPosition();
        }, DispatcherPriority.Background);
    }

    public void OnVideoModeChanged(object? sender, VideoModeChangedEventArgs e) {
        _uiDispatcher.Post(() => {
            AspectRatioCorrectionFactor = e.AspectRatioCorrectionFactor;
            if (_loggerService.IsEnabled(LogLevel.Debug)) {
                _loggerService.LogDebug(
                    "Video mode changed to {Width}x{Height}, aspect ratio correction factor: {Factor}",
                    e.NewMode.Width, e.NewMode.Height, e.AspectRatioCorrectionFactor);
            }
        });
    }

    public void ShowMouseCursor() => _uiDispatcher.Post(() => ShowCursor = true);
    public void HideMouseCursor() => _uiDispatcher.Post(() => ShowCursor = false);

    internal void OnKeyDown(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        KeyDown?.Invoke(this,
            new KeyboardEventArgs((Shared.Emulator.Keyboard.PhysicalKey)e.PhysicalKey, IsPressed: true));
    }

    internal void OnKeyUp(KeyEventArgs e) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        KeyUp?.Invoke(this,
            new KeyboardEventArgs((Shared.Emulator.Keyboard.PhysicalKey)e.PhysicalKey, IsPressed: false));
    }

    public void OnMouseButtonDown(PointerPressedEventArgs e, Image image) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        Avalonia.Input.MouseButton mouseButton =
            e.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonDown?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, true));
    }

    public void OnMouseButtonUp(PointerReleasedEventArgs e, Image image) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        Avalonia.Input.MouseButton mouseButton =
            e.GetCurrentPoint(image).Properties.PointerUpdateKind.GetMouseButton();
        MouseButtonUp?.Invoke(this, new MouseButtonEventArgs((MouseButton)mouseButton, false));
    }

    public void OnMouseMoved(PointerEventArgs e, Image image) {
        if (image.Source is null || _pauseHandler.IsPaused) {
            return;
        }
        MouseX = e.GetPosition(image).X / image.Source.Size.Width;
        MouseY = e.GetPosition(image).Y / image.Source.Size.Height;
        MouseMoved?.Invoke(this, new MouseMoveEventArgs(MouseX, MouseY));
        UpdateShownEmulatorMouseCursorPosition();
    }

    /// <summary>
    /// Receives pre-normalised pointer coordinates (0..1) from relative-mode mouse capture backends.
    /// </summary>
    internal void OnMouseMovedNormalized(double x, double y) {
        if (_pauseHandler.IsPaused) {
            return;
        }
        MouseX = x;
        MouseY = y;
        MouseMoved?.Invoke(this, new MouseMoveEventArgs(x, y));
        UpdateShownEmulatorMouseCursorPosition();
    }

    internal void NotifyAppClosing() => _isAppClosing = true;

    private void DrawScreen() {
        if (_disposed || _pauseHandler.IsPaused || _isSettingResolution ||
            _isAppClosing || Bitmap is null || RenderScreen is null) {
            return;
        }
        using ILockedFramebuffer pixels = Bitmap.Lock();
        UIRenderEventArgs args = new UIRenderEventArgs(pixels.Address,
            pixels.RowBytes * pixels.Size.Height / 4);
        RenderScreen.Invoke(this, args);
        InvalidateBitmap?.Invoke();
    }

    private void UpdateShownEmulatorMouseCursorPosition() {
        MouseStatusRecord status = _sharedMouseData.CurrentMouseStatus;
        EmulatorMouseCursorInfo = $"X: {status.X} Y: {status.Y}";
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _drawTimer?.Stop();
        _drawTimer = null;
        _uiDispatcher.Post(() => {
            Bitmap?.Dispose();
            Cursor?.Dispose();
        }, DispatcherPriority.MaxValue);
    }
}
