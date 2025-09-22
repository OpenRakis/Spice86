namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Implements a queue for handling and processing keyboard and mouse events. <br/>
/// Used by the emulation thread to avoid the UI thread modifying keyboard state via events,
/// while the emulator thread is reading the <see cref="Intel8042Controller"/> at the same time. <br/>
/// Same deal for the Mouse events. If Joystick support is implemented, joystick UI events will also pass through here.
/// </summary>
public class InputEventQueue : IGuiKeyboardEvents, IGuiMouseEvents {
    private readonly Queue<Action> _eventQueue = new();
    private readonly IGuiMouseEvents? _mouseEvents;
    private readonly IGuiKeyboardEvents? _keyboardEvents;

    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;

    public InputEventQueue(
        IGuiKeyboardEvents? keyboardEvents = null,
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
    /// Processes all pending input events in the event queue.
    /// </summary>
    internal void ProcessAllPendingInputEvents() {
        while (_eventQueue.TryDequeue(out Action? top)) {
            top.Invoke();
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