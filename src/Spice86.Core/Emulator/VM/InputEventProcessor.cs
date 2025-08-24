namespace Spice86.Core.Emulator.VM;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.Input.Keyboard;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

/// <summary>
/// Processes GUI input events and forwards them to the appropriate devices.
/// This class collects keyboard events from the GUI for the EmulationLoop to process them each tick.
/// </summary>
public class InputEventProcessor
{
    private readonly ILoggerService _loggerService;
    private readonly Keyboard _keyboard;
    private readonly Queue<KeyboardEventArgs> _keyboardEventQueue = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InputEventProcessor"/> class.
    /// </summary>
    /// <param name="keyboard">The keyboard device to process events for</param>
    /// <param name="loggerService">The logger service implementation</param>
    /// <param name="gui">The GUI that provides input events</param>
    public InputEventProcessor(Keyboard keyboard, ILoggerService loggerService,
        IGui? gui = null)
    {
        _keyboard = keyboard;
        _loggerService = loggerService;

        // Subscribe to GUI events if available
        if (gui != null)
        {
            gui.KeyDown += OnKeyDown;
            gui.KeyUp += OnKeyUp;
        }
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e)
    {
        // Queue the event instead of processing it immediately
        _keyboardEventQueue.Enqueue(e);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose))
        {
            _loggerService.Verbose("Key down event queued: {Key}", e.Key);
        }
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e)
    {
        // Queue the event instead of processing it immediately
        _keyboardEventQueue.Enqueue(e);
        if (_loggerService.IsEnabled(LogEventLevel.Verbose))
        {
            _loggerService.Verbose("Key up event queued: {Key}", e.Key);
        }
    }

    /// <summary>
    /// Processes all queued keyboard events.
    /// Called at the end of each emulation loop cycle.
    /// </summary>
    public void ProcessEvents()
    {
        // Process all queued events
        if (_keyboardEventQueue.TryDequeue(out KeyboardEventArgs e))
        {
            _keyboard.AddKeyToBuffer(e);
        }
    }
}