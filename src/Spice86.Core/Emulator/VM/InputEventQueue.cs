namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System;
using System.Threading;

/// <summary>
/// Represents a queue for handling and processing keyboard and mouse events. <br/>
/// Used by the emulation loop thread to avoid the UI thread modifying keyboard state via events,
/// while the emulator thread is reading the keyboard via the same instance of the <see cref="Intel8042Controller"/> class. <br/>
/// Same deal for the Mouse event. If Joystick support is implemented, joystick UI events will also pass through here.
/// </summary>
/// <remarks>
/// Input events are processed at tick boundaries (~1000 times/sec), matching DOSBox's
/// <c>GFX_PollAndHandleEvents()</c> which runs between ticks in <c>normal_loop()</c>.
/// A simple <see cref="Queue{T}"/> with a lock is sufficient at this frequency.
/// </remarks>
public class InputEventHub : IGuiKeyboardEvents, IGuiMouseEvents {
    private readonly Queue<Action> _eventQueue = new();
    private readonly Lock _lock = new();
    private readonly IGuiMouseEvents? _mouseEvents;
    private readonly IGuiKeyboardEvents? _keyboardEvents;

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;

    public InputEventHub(IGuiKeyboardEvents? keyboardEvents = null,
        IGuiMouseEvents? mouseEvents = null) {
        if (keyboardEvents is not null) {
            _keyboardEvents = keyboardEvents;
            _keyboardEvents.KeyDown += OnKeyDown;
            _keyboardEvents.KeyUp += OnKeyUp;
        }
        if (mouseEvents is not null) {
            _mouseEvents = mouseEvents;
            _mouseEvents.MouseMoved += OnMouseMoved;
            _mouseEvents.MouseButtonDown += OnMouseButtonDown;
            _mouseEvents.MouseButtonUp += OnMouseButtonUp;
        }
    }

    private void Enqueue(Action action) {
        lock (_lock) {
            _eventQueue.Enqueue(action);
        }
    }

    private void OnMouseMoved(object? sender, MouseMoveEventArgs e) =>
        Enqueue(() => MouseMoved?.Invoke(sender, e));

    private void OnMouseButtonUp(object? sender, MouseButtonEventArgs e) =>
        Enqueue(() => MouseButtonUp?.Invoke(sender, e));

    private void OnMouseButtonDown(object? sender, MouseButtonEventArgs e) =>
        Enqueue(() => MouseButtonDown?.Invoke(sender, e));

    private void OnKeyUp(object? sender, KeyboardEventArgs e) =>
        Enqueue(() => KeyUp?.Invoke(sender, e));

    private void OnKeyDown(object? sender, KeyboardEventArgs e) =>
        Enqueue(() => KeyDown?.Invoke(sender, e));

    internal void ProcessAllPendingInputEvents() {
        lock (_lock) {
            while (_eventQueue.TryDequeue(out Action? action)) {
                action.Invoke();
            }
        }
    }

    public double MouseX {
        get => _mouseEvents?.MouseX ?? 0;
        set { if (_mouseEvents is not null) { _mouseEvents.MouseX = value; } } }

    public double MouseY {
        get => _mouseEvents?.MouseY ?? 0;
        set { if (_mouseEvents is not null) { _mouseEvents.MouseY = value; } } }

    public void HideMouseCursor() => _mouseEvents?.HideMouseCursor();

    public void ShowMouseCursor() => _mouseEvents?.ShowMouseCursor();
}