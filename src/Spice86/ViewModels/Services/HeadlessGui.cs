namespace Spice86.ViewModels.Services;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

/// <inheritdoc cref="IGuiVideoPresentation" />
public sealed class HeadlessGui : IGuiVideoPresentation, IGuiMouseEvents,
    IGuiKeyboardEvents, IGuiJoystickEvents, IDisposable {
    private const double ScreenRefreshHz = 60;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1000.0 / ScreenRefreshHz);

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

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
#pragma warning disable CS0067 // Headless GUI never raises these events
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    public event Action? UserInterfaceInitialized;
#pragma warning restore CS0067

    /// <inheritdoc />
    public event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;

    /// <summary>
    /// Simulates a key press event, firing <see cref="KeyDown"/> into the full input pipeline.
    /// </summary>
    /// <param name="key">The physical key to press.</param>
    public void SimulateKeyPress(PhysicalKey key) {
        KeyDown?.Invoke(this, new KeyboardEventArgs(key, IsPressed: true));
    }

    /// <summary>
    /// Simulates a key release event, firing <see cref="KeyUp"/> into the full input pipeline.
    /// </summary>
    /// <param name="key">The physical key to release.</param>
    public void SimulateKeyRelease(PhysicalKey key) {
        KeyUp?.Invoke(this, new KeyboardEventArgs(key, IsPressed: false));
    }

    /// <summary>
    /// Simulates a joystick axis event, firing <see cref="JoystickAxisChanged"/> into the
    /// input pipeline. Used by headless integration tests and scripted-input replay.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick index (0 or 1).</param>
    /// <param name="axis">Logical axis being moved.</param>
    /// <param name="value">Normalized axis value in <c>[-1.0, 1.0]</c>.</param>
    public void SimulateJoystickAxis(int stickIndex, JoystickAxis axis, float value) {
        JoystickAxisChanged?.Invoke(this, new JoystickAxisEventArgs(stickIndex, axis, value));
    }

    /// <summary>
    /// Simulates a joystick button event, firing <see cref="JoystickButtonChanged"/>.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick index (0 or 1).</param>
    /// <param name="buttonIndex">Zero-based logical button index (0..3).</param>
    /// <param name="isPressed">Whether the button is being pressed.</param>
    public void SimulateJoystickButton(int stickIndex, int buttonIndex, bool isPressed) {
        JoystickButtonChanged?.Invoke(this,
            new JoystickButtonEventArgs(stickIndex, buttonIndex, isPressed));
    }

    /// <summary>
    /// Simulates a joystick hat (POV) direction change.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick index (0 or 1).</param>
    /// <param name="direction">New hat direction.</param>
    public void SimulateJoystickHat(int stickIndex, JoystickHatDirection direction) {
        JoystickHatChanged?.Invoke(this, new JoystickHatEventArgs(stickIndex, direction));
    }

    /// <summary>
    /// Simulates a virtual stick connect/disconnect event.
    /// </summary>
    /// <param name="stickIndex">Zero-based stick index (0 or 1).</param>
    /// <param name="isConnected"><see langword="true"/> for connect.</param>
    /// <param name="deviceName">Friendly device name; empty when disconnecting.</param>
    /// <param name="deviceGuid">SDL joystick GUID; empty when unknown
    /// or disconnecting. Forwarded to <c>JoystickProfileActivator</c>
    /// so GUID-based profile matching can take precedence.</param>
    public void SimulateJoystickConnection(int stickIndex, bool isConnected, string deviceName, string deviceGuid = "") {
        JoystickConnectionChanged?.Invoke(this,
            new JoystickConnectionEventArgs(stickIndex, isConnected, deviceName, deviceGuid));
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public double MouseX { get; set; }

    public double MouseY { get; set; }

    public void SetResolution(int width, int height) {
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException($"Invalid resolution: {width}x{height}");
        }

        _isSettingResolution = true;
        try {
            if (Width != width || Height != height) {
                Width = width;
                Height = height;
                if (_disposed) {
                    return;
                }

                int bufferSize = width * height * 4;

                if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize) {
                    _pixelBuffer = new byte[bufferSize];
                }

                Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
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
        byte[]? pixelBuffer = _pixelBuffer;
        if (_disposed || _isSettingResolution || _isAppClosing || pixelBuffer is null || RenderScreen is null) {
            return;
        }

        fixed (byte* bufferPtr = _pixelBuffer) {
            int rowBytes = Width * 4; // 4 bytes per pixel (BGRA)
            int length = rowBytes * Height / 4;

            UIRenderEventArgs uiRenderEventArgs = new UIRenderEventArgs((IntPtr)bufferPtr, length);
            RenderScreen.Invoke(this, uiRenderEventArgs);
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

        // Stop the timer to prevent any new callbacks
        _drawTimer?.Dispose();

        _pixelBuffer = null;
    }
}
