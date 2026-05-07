namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Input.Joystick;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Represents a queue for handling and processing keyboard, mouse and joystick events. <br/>
/// Used by the emulation loop thread to avoid the UI thread modifying device state via events,
/// while the emulator thread is reading the same instance of <see cref="Intel8042Controller"/> /
/// the gameport device. <br/>
/// All UI input (keyboard, mouse, joystick) flows through this hub so that events are replayed
/// on the emulator thread in the order they happened on the UI thread.
/// </summary>
/// <remarks>This class provides a mechanism to enqueue and process input events in a controlled manner. It wraps 
/// around implementations of <see cref="IGuiKeyboardEvents"/>, <see cref="IGuiMouseEvents"/> and
/// <see cref="IGuiJoystickEvents"/> to capture and queue their events. The queued events can then
/// be processed one at a time using the <see cref="ProcessAllPendingInputEvents"/> method.
/// The <see cref="InputEventHub"/> also exposes properties and methods for interacting with mouse  coordinates and
/// cursor visibility, delegating these operations to the underlying implementation, if available.</remarks>
public class InputEventHub : IGuiKeyboardEvents, IGuiMouseEvents, IGuiJoystickEvents {
    private readonly Queue<Action> _eventQueue = new();
    // a thread-safe queue, accessed by both UI thread and emulation thread, requires a lock.
    private readonly object _lock = new();
    private readonly IGuiMouseEvents? _mouseEvents;
    private readonly IGuiKeyboardEvents? _keyboardEvents;
    private readonly IGuiJoystickEvents? _joystickEvents;

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;

    /// <inheritdoc />
    public event EventHandler<JoystickAxisEventArgs>? JoystickAxisChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickButtonEventArgs>? JoystickButtonChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickHatEventArgs>? JoystickHatChanged;

    /// <inheritdoc />
    public event EventHandler<JoystickConnectionEventArgs>? JoystickConnectionChanged;

    public InputEventHub(IGuiKeyboardEvents? keyboardEvents = null,
        IGuiMouseEvents? mouseEvents = null,
        IGuiJoystickEvents? joystickEvents = null) {
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
        if (joystickEvents is not null) {
            _joystickEvents = joystickEvents;
            _joystickEvents.JoystickAxisChanged += OnJoystickAxisChanged;
            _joystickEvents.JoystickButtonChanged += OnJoystickButtonChanged;
            _joystickEvents.JoystickHatChanged += OnJoystickHatChanged;
            _joystickEvents.JoystickConnectionChanged += OnJoystickConnectionChanged;
        }
    }

    private void Enqueue(Action action) {
        lock (_lock) {
            _eventQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// Thread-safe hook for non-UI producers (e.g. the MCP server) that need to
    /// mutate emulator input state. The action is enqueued under the same lock
    /// as UI events and will run on the emulator thread the next time
    /// <see cref="ProcessAllPendingInputEvents"/> is pumped.
    /// </summary>
    public void PostToEmulatorThread(Action action) => Enqueue(action);

    /// <summary>
    /// Enqueues a keyboard event to be fired on the emulator thread, following
    /// the same path as UI keyboard events (PhysicalKey → PS2Keyboard scancode pipeline).
    /// </summary>
    public void PostKeyboardEvent(KeyboardEventArgs e) {
        if (e.IsPressed) {
            Enqueue(() => KeyDown?.Invoke(null, e));
        } else {
            Enqueue(() => KeyUp?.Invoke(null, e));
        }
    }

    /// <summary>
    /// Enqueues a mouse move event to be fired on the emulator thread, following the same
    /// path as UI pointer events. Coordinates are normalized (0.0–1.0 relative to the screen).
    /// </summary>
    public void PostMouseMoveEvent(MouseMoveEventArgs e) =>
        Enqueue(() => MouseMoved?.Invoke(null, e));

    /// <summary>
    /// Enqueues a mouse button event to be fired on the emulator thread, following the same
    /// path as UI pointer events.
    /// </summary>
    public void PostMouseButtonEvent(MouseButtonEventArgs e) {
        if (e.ButtonDown) {
            Enqueue(() => MouseButtonDown?.Invoke(null, e));
        } else {
            Enqueue(() => MouseButtonUp?.Invoke(null, e));
        }
    }

    /// <summary>
    /// Enqueues a joystick axis event to be fired on the emulator thread, following the
    /// same path as UI joystick events (raw SDL input -> profile mapping -> logical event).
    /// </summary>
    public void PostJoystickAxisEvent(JoystickAxisEventArgs e) =>
        Enqueue(() => JoystickAxisChanged?.Invoke(null, e));

    /// <summary>
    /// Enqueues a joystick button event to be fired on the emulator thread.
    /// </summary>
    public void PostJoystickButtonEvent(JoystickButtonEventArgs e) =>
        Enqueue(() => JoystickButtonChanged?.Invoke(null, e));

    /// <summary>
    /// Enqueues a joystick hat (POV) event to be fired on the emulator thread.
    /// </summary>
    public void PostJoystickHatEvent(JoystickHatEventArgs e) =>
        Enqueue(() => JoystickHatChanged?.Invoke(null, e));

    /// <summary>
    /// Enqueues a joystick connection event (hot-plug, profile change, or simulated
    /// connect/disconnect) to be fired on the emulator thread.
    /// </summary>
    public void PostJoystickConnectionEvent(JoystickConnectionEventArgs e) =>
        Enqueue(() => JoystickConnectionChanged?.Invoke(null, e));

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

    private void OnJoystickAxisChanged(object? sender, JoystickAxisEventArgs e) =>
        Enqueue(() => JoystickAxisChanged?.Invoke(sender, e));

    private void OnJoystickButtonChanged(object? sender, JoystickButtonEventArgs e) =>
        Enqueue(() => JoystickButtonChanged?.Invoke(sender, e));

    private void OnJoystickHatChanged(object? sender, JoystickHatEventArgs e) =>
        Enqueue(() => JoystickHatChanged?.Invoke(sender, e));

    private void OnJoystickConnectionChanged(object? sender, JoystickConnectionEventArgs e) =>
        Enqueue(() => JoystickConnectionChanged?.Invoke(sender, e));

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
        set { if (_mouseEvents is not null) { _mouseEvents.MouseX = value; } }
    }

    public double MouseY {
        get => _mouseEvents?.MouseY ?? 0;
        set { if (_mouseEvents is not null) { _mouseEvents.MouseY = value; } }
    }

    public void HideMouseCursor() => _mouseEvents?.HideMouseCursor();

    public void ShowMouseCursor() => _mouseEvents?.ShowMouseCursor();
}