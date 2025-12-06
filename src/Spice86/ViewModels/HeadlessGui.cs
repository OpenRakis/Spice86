namespace Spice86.ViewModels;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

/// <inheritdoc cref="Spice86.Shared.Interfaces.IGuiVideoPresentation" />
public sealed class HeadlessGui : IGuiVideoPresentation, IGuiMouseEvents,
    IGuiKeyboardEvents, IDisposable {
    private const double ScreenRefreshHz = 60;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1000.0 / ScreenRefreshHz);
    private readonly SemaphoreSlim? _drawingSemaphoreSlim = new(1, 1);

    private bool _disposed;

    private Timer? _drawTimer;
    private bool _isAppClosing;
    private bool _isSettingResolution;

    private byte[]? _pixelBuffer;
    private bool _renderingTimerInitialized;

    public HeadlessGui() {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnProcessExit;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void ShowMouseCursor() {
    }

    public void HideMouseCursor() {
    }

#pragma warning disable CS0067 // Headless GUI never raises these events
    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    public event Action? UserInterfaceInitialized;
#pragma warning restore CS0067

    public int Width { get; private set; }

    public int Height { get; private set; }

    public double MouseX { get; set; }

    public double MouseY { get; set; }

    public void SetResolution(int width, int height, double pixelAspectRatio = 1.0) {
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException($"Invalid resolution: {width}x{height}");
        }

        // Headless mode doesn't need to apply pixel aspect ratio correction
        // since there's no visual display, but we accept the parameter for interface compliance

        _isSettingResolution = true;
        try {
            if (Width != width || Height != height) {
                Width = width;
                Height = height;
                if (_disposed) {
                    return;
                }

                int bufferSize = width * height * 4;

                _drawingSemaphoreSlim?.Wait();
                try {
                    if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize) {
                        _pixelBuffer = new byte[bufferSize];
                    }

                    Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
                } finally {
                    if (!_disposed) {
                        _drawingSemaphoreSlim?.Release();
                    }
                }
            }
        } finally {
            _isSettingResolution = false;
        }

        InitializeRenderingTimer();
    }

    private void OnProcessExit(object? sender, EventArgs e) {
        _isAppClosing = true;
    }

    private void InitializeRenderingTimer() {
        if (_renderingTimerInitialized) {
            return;
        }

        _renderingTimerInitialized = true;
        _drawTimer = new Timer(DrawScreenCallback, null, RefreshInterval, RefreshInterval);
    }

    private void DrawScreenCallback(object? state) {
        DrawScreen();
    }

    private unsafe void DrawScreen() {
        if (_disposed || _isSettingResolution || _isAppClosing || _pixelBuffer is null || RenderScreen is null) {
            return;
        }

        _drawingSemaphoreSlim?.Wait();
        try {
            fixed (byte* bufferPtr = _pixelBuffer) {
                int rowBytes = Width * 4; // 4 bytes per pixel (BGRA)
                int length = rowBytes * Height / 4;

                var uiRenderEventArgs = new UIRenderEventArgs((IntPtr)bufferPtr, length);
                RenderScreen.Invoke(this, uiRenderEventArgs);
            }
        } finally {
            if (!_disposed) {
                _drawingSemaphoreSlim?.Release();
            }
        }
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (!disposing) {
            return;
        }

        // Stop the timer first to prevent any new callbacks
        _drawTimer?.Dispose();

        // Wait for any ongoing draw operation to complete
        // This prevents a race condition where the timer callback
        // is in the middle of rendering when we dispose resources
        try {
            _drawingSemaphoreSlim?.Wait(TimeSpan.FromMilliseconds(100));
            _drawingSemaphoreSlim?.Release();
        } catch (ObjectDisposedException) {
            // Semaphore was already disposed, which is fine
        }

        _pixelBuffer = null;
        _drawingSemaphoreSlim?.Dispose();
    }
}