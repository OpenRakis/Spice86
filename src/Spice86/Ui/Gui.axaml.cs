namespace Spice86.UI;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Serilog;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.Controls;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// GUI of the emulator.<br/>
/// <ul>
/// <li>Displays the content of the video ram (when the emulator requests it)</li>
/// <li>Communicates keyboard and mouse events to the emulator</li>
/// </ul>
/// </summary>
public partial class Gui : UserControl {
    private static readonly ILogger _logger = Log.Logger.ForContext<Gui>();
    private int mainCanvasScale = 4;
    private int width = 1;
    private int height = 1;
    private Key? lastKeyCode = null;
    private List<Key> keysPressed = new();
    private int mouseX;
    private int mouseY;
    private bool leftButtonClicked;
    private bool rightButtonClicked;
    private Action? onKeyPressedEvent;
    private Action? onKeyReleasedEvent;

    // Dictionary associating a start address to a ScalableBitmapControl
    public Dictionary<uint, VideoBuffer> VideoBuffers {
        get => GetValue(VideoBuffersProperty);
        set {
            if (GetValue(VideoBuffersProperty) != value) {
                SetValue(VideoBuffersProperty, value);
            }
        }
    }

    public static StyledProperty<Dictionary<uint, VideoBuffer>> VideoBuffersProperty =
        AvaloniaProperty.Register<Gui, Dictionary<uint, VideoBuffer>>(nameof(VideoBuffers), new());
    public Gui() {
        InitializeComponent();
        SetResolution(320, 200, 0);
        this.Cursor = null;
        this.KeyDown += (s, e) => this.OnKeyPressed(e);
        this.KeyUp += (s, e) => this.OnKeyReleased(e);
    }

    private void InitializeComponent() {
        AvaloniaXamlLoader.Load(this);
    }

    public int GetWidth() {
        return width;
    }

    public int GetHeight() {
        return height;
    }

    public Key? GetLastKeyCode() {
        return lastKeyCode;
    }

    public bool IsKeyPressed(Key keyCode) {
        return keysPressed.Contains(keyCode);
    }

    public int GetMouseX() {
        return mouseX;
    }

    public int GetMouseY() {
        return mouseY;
    }

    public void SetMouseX(int mouseX) {
        this.mouseX = mouseX;
    }

    public void SetMouseY(int mouseY) {
        this.mouseY = mouseY;
    }

    public bool IsLeftButtonClicked() {
        return leftButtonClicked;
    }

    public bool IsRightButtonClicked() {
        return rightButtonClicked;
    }

    private void OnKeyPressed(KeyEventArgs @event) {
        Key keyCode = @event.Key;
        if (!keysPressed.Contains(keyCode)) {
            _logger.Information("Key pressed {@KeyPressed}", keyCode);
            keysPressed.Add(keyCode);
            this.lastKeyCode = keyCode;
            RunOnKeyEvent(this.onKeyPressedEvent);
        }
    }

    private void OnKeyReleased(KeyEventArgs @event) {
        this.lastKeyCode = @event.Key;
        _logger.Information("Key released {@LastKeyCode}", lastKeyCode);
        keysPressed.Remove(lastKeyCode.Value);
        RunOnKeyEvent(this.onKeyReleasedEvent);
    }

    private void RunOnKeyEvent(Action? runnable) {
        if (runnable != null) {
            runnable.Invoke();
        }
    }

    public void SetOnKeyPressedEvent(Action onKeyPressedEvent) {
        this.onKeyPressedEvent = onKeyPressedEvent;
    }

    public void SetOnKeyReleasedEvent(Action onKeyReleasedEvent) {
        this.onKeyReleasedEvent = onKeyReleasedEvent;
    }

    private void OnMouseMoved(PointerEventArgs @event) {
        SetMouseX((int)@event.GetPosition(this).X);
        SetMouseY((int)@event.GetPosition(this).Y);
    }

    private void OnMouseClick(PointerEventArgs @event, bool click) {
        if (@event.Pointer.IsPrimary) {
            this.leftButtonClicked = click;
        }

        if (@event.Pointer.IsPrimary == false) {
            rightButtonClicked = click;
        }
    }

    public void SetResolution(int width, int height, uint address) {
        VideoBuffers.Clear();
        this.width = width;
        this.height = height;
        AddBuffer(address, mainCanvasScale, width, height, (canvas) => {
            canvas.PointerMoved += (s, e) => this.OnMouseMoved(e);
            canvas.PointerPressed += (s, e) => this.OnMouseClick(e, true);
            canvas.PointerReleased += (s, e) => this.OnMouseClick(e, false);
        });
    }

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, Action<ScalableBitmapControl> canvasPostSetupAction) {
        VideoBuffer videoBuffer = new VideoBuffer(bufferWidth, bufferHeight, scale, address, VideoBuffers.Count);
        ScalableBitmapControl canvas = videoBuffer.GetScalableControl();
        VideoBuffers.Add(address, videoBuffer);
        if (canvasPostSetupAction != null) {
            canvasPostSetupAction.Invoke(canvas);
        }

    }

    private IEnumerable<VideoBuffer> SortedBuffers() {
        return VideoBuffers.OrderBy(x => x).Select(x => x.Value);
    }

    public void RemoveBuffer(uint address) {
        VideoBuffers.Remove(address);
    }

    public void Draw(byte[] memory, Rgb[] palette) {
        foreach (VideoBuffer videoBuffer in SortedBuffers()) {
            videoBuffer.Draw(memory, palette);
        }
    }

    public Dictionary<uint, VideoBuffer> GetVideoBuffers() {
        return VideoBuffers;
    }
}
