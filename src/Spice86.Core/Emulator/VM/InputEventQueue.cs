namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Represents a queue for handling and processing keyboard and mouse events. <br/>
/// Used by the emulation loop thread to avoid the UI thread modifying keyboard state via events,
/// while the emulator thread is reading the keyboard via the same instance of the <see cref="Intel8042Controller"/> class. <br/>
/// Same deal for the Mouse event. If Joystick support is implemented, joystick UI events will also pass through here.
/// </summary>
/// <remarks>This class provides a mechanism to enqueue and process input events in a controlled manner. It wraps 
/// around implementations of <see cref="IGuiKeyboardEvents"/> and <see cref="IGuiMouseEvents"/> to capture  and queue
/// their events. The queued events can then be processed one at a time using the  <see cref="ProcessAllPendingInputEvents"/> method.
/// The <see cref="InputEventHub"/> also exposes properties and methods for interacting with mouse  coordinates and
/// cursor visibility, delegating these operations to the underlying implementation, if available.</remarks>
public class InputEventHub : IGuiKeyboardEvents, IGuiMouseEvents {
    private readonly Queue<Action> _eventQueue = new();
    // a thread-safe queue, accessed by both UI thread and emulation thread, requires a lock.
    private readonly object _lock = new();
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
        if (_eventQueue.Count == 0) {
            return;
        }
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