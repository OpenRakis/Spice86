namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Concurrent;

/// <summary>
/// Implements a thread-safe queue for handling and processing keyboard and mouse events. <br/>
/// Used by the emulation thread to avoid the UI thread modifying keyboard state via events,
/// while the emulator thread is reading the <see cref="Intel8042Controller"/> at the same time. <br/>
/// Same deal for the Mouse events. If Joystick support is implemented, joystick UI events will also pass through here.
/// </summary>
public class InputEventQueue : IGuiKeyboardEvents, IGuiMouseEvents, IDisposable {
    private readonly ConcurrentQueue<Action> _eventQueue = new();
    private readonly IGuiMouseEvents _mouseEvents;
    private readonly IGuiKeyboardEvents _keyboardEvents;

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;

    public InputEventQueue(
        IGuiKeyboardEvents keyboardEvents,
        IGuiMouseEvents mouseEvents) {
        _keyboardEvents = keyboardEvents;
        _mouseEvents = mouseEvents;

        _keyboardEvents.KeyDown += OnKeyDown;
        _keyboardEvents.KeyUp += OnKeyUp;
        _mouseEvents.MouseMoved += OnMouseMoved;
        _mouseEvents.MouseButtonDown += OnMouseButtonDown;
        _mouseEvents.MouseButtonUp += OnMouseButtonUp;
    }

    private void OnMouseMoved(object? sender, MouseMoveEventArgs e) =>
        _eventQueue.Enqueue(() => MouseMoved?.Invoke(sender, e));

    private void OnMouseButtonUp(object? sender, MouseButtonEventArgs e) =>
        _eventQueue.Enqueue(() => MouseButtonUp?.Invoke(sender, e));

    private void OnMouseButtonDown(object? sender, MouseButtonEventArgs e) =>
        _eventQueue.Enqueue(() => MouseButtonDown?.Invoke(sender, e));

    private void OnKeyUp(object? sender, KeyboardEventArgs e) =>
        _eventQueue.Enqueue(() => KeyUp?.Invoke(sender, e));

    private void OnKeyDown(object? sender, KeyboardEventArgs e) =>
        _eventQueue.Enqueue(() => KeyDown?.Invoke(sender, e));

    /// <summary>
    /// Gets whether there are any pending input events in the queue.
    /// </summary>
    public bool HasPendingEvents => !_eventQueue.IsEmpty;

    /// <summary>
    /// Processes all pending input events in the event queue.
    /// </summary>
    public void ProcessAllPendingInputEvents() {
        while (_eventQueue.TryDequeue(out Action? top)) {
            top.Invoke();
        }
    }

    /// <summary>
    /// Unsubscribes from event sources to prevent memory leaks.
    /// </summary>
    public void Dispose() {
        _keyboardEvents.KeyDown -= OnKeyDown;
        _keyboardEvents.KeyUp -= OnKeyUp;
        _mouseEvents.MouseMoved -= OnMouseMoved;
        _mouseEvents.MouseButtonDown -= OnMouseButtonDown;
        _mouseEvents.MouseButtonUp -= OnMouseButtonUp;
    }

    public double MouseX {
        get => _mouseEvents.MouseX;
        set => _mouseEvents.MouseX = value;
    }

    public double MouseY {
        get => _mouseEvents.MouseY;
        set => _mouseEvents.MouseY = value;
    }

    public void HideMouseCursor() => _mouseEvents.HideMouseCursor();

    public void ShowMouseCursor() => _mouseEvents.ShowMouseCursor();
}