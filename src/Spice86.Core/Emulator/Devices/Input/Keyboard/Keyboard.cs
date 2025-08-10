namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Implementation of a keyboard with typematic (auto-repeat) functionality
/// </summary>
public sealed class Keyboard : DefaultIOPortHandler {
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;
    
    // Add a queue to store keyboard events
    private readonly Queue<KeyboardEventArgs> _keyboardBuffer = new Queue<KeyboardEventArgs>();
    // Buffer size - typical IBM PC/AT keyboard buffer could hold 16 scan codes
    private const int MaxBufferSize = 16;
    // Flag to track if there are unread keys in the buffer
    private bool _hasUnreadKeys = false;
    
    // Current keyboard state to be checked by the emulation loop
    private KeyboardState _currentState = new KeyboardState();
    
    // Typematic (auto-repeat) functionality
    private KeyboardEventArgs _currentlyHeldKey = KeyboardEventArgs.None;
    private bool _typematicActive = false;
    private long _lastKeyPressTimeTicks;
    
    // High precision timer for keyboard timing
    private readonly Stopwatch _keyboardTimer = Stopwatch.StartNew();
    
    // Default typematic parameters (can be configured via keyboard commands)
    private TimeSpan _typematicDelay = TimeSpan.FromMilliseconds(500); // ~500ms initial delay
    private TimeSpan _typematicRate = TimeSpan.FromMilliseconds(100);  // ~10Hz repeat rate (100ms)
    
    // Debounce functionality to prevent accidental double inputs
    private readonly Dictionary<Key, long> _lastKeyEventTicks = new Dictionary<Key, long>();
    private const int DebounceThresholdMs = 10; // Minimum time between key events (10ms is typical)
    private readonly long _debounceThresholdTicks;

    /// <summary>
    /// The current keyboard command, such as 'Perform self-test' (0xAA)
    /// </summary>
    public KeyboardCommand Command { get; private set; } = KeyboardCommand.None;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte SystemTestStatusMask = 1<<2;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte KeyboardEnableStatusMask = 1<<4;

    /// <summary>
    /// Output buffer full status bit
    /// </summary>
    public const byte OutputBufferFullMask = 1<<0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keyboard"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="a20Gate">The class that controls whether the CPU's 20th address line is enabled.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public Keyboard(State state, IOPortDispatcher ioPortDispatcher, A20Gate a20Gate, DualPic dualPic,
        ILoggerService loggerService, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        _debounceThresholdTicks = Stopwatch.Frequency * DebounceThresholdMs / 1000;
        
        InitPortHandlers(ioPortDispatcher);
    }

    /// <summary>
    /// Updates the keyboard state from the GUI and processes all pending events
    /// </summary>
    /// <param name="gui">The GUI instance that provides keyboard state</param>
    public void Update(IGui? gui) {
        if (gui == null) {
            return;
        }
        
        // Process any new key state changes from the GUI
        ProcessKeyChanges(gui);
        
        // Handle typematic repeat if needed
        UpdateTypematic();
    }
    
    /// <summary>
    /// Process key changes from the GUI
    /// </summary>
    private void ProcessKeyChanges(IGui gui) {
        // Get current key states from the GUI
        KeyboardState newState = GetKeyboardStateFromGui(gui);
        
        // Compare with previous state to detect changes
        foreach (Key key in Enum.GetValues(typeof(Key))) {
            bool wasPressed = _currentState.IsKeyPressed(key);
            bool isPressed = newState.IsKeyPressed(key);
            
            if (wasPressed != isPressed) {
                // Key state changed
                if (isPressed) {
                    ProcessKeyDown(key);
                } else {
                    ProcessKeyUp(key);
                }
            }
        }
        
        // Save current state for next comparison
        _currentState = newState;
    }
    
    /// <summary>
    /// Get the current keyboard state from the GUI
    /// </summary>
    private KeyboardState GetKeyboardStateFromGui(IGui gui) {
        KeyboardState state = new KeyboardState();
        
        // This would need to be implemented in the IGui interface
        // For now, we'll maintain state internally
        
        return state;
    }

    /// <summary>
    /// Process a key down event
    /// </summary>
    private void ProcessKeyDown(Key key) {
        // Create a keyboard event for this key
        KeyboardEventArgs e = CreateKeyboardEventArgs(key, true);
        
        if (!e.ScanCode.HasValue) {
            return;
        }
        
        // Apply debounce - ignore if key was pressed too recently
        if (ShouldDebounce(key)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Debouncing key down: {Key}", key);
            }
            return;
        }
        
        // Update last event time for this key
        _lastKeyEventTicks[key] = _keyboardTimer.ElapsedTicks;
        
        // Store the key as currently held and note the press time
        _currentlyHeldKey = e;
        _lastKeyPressTimeTicks = _keyboardTimer.ElapsedTicks;
        _typematicActive = false;
        
        // Add to buffer if there's space (initial key press)
        if (_keyboardBuffer.Count < MaxBufferSize) {
            _keyboardBuffer.Enqueue(e);
            _hasUnreadKeys = true;
            
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Key down processed: Key={Key}, ScanCode={ScanCode:X2}", 
                    key, e.ScanCode.Value);
            }
            
            // Trigger interrupt for new key
            _dualPic.ProcessInterruptRequest(1);
        }
    }
    
    /// <summary>
    /// Process a key up event
    /// </summary>
    private void ProcessKeyUp(Key key) {
        // Create a keyboard event for this key
        KeyboardEventArgs e = CreateKeyboardEventArgs(key, false);
        
        if (!e.ScanCode.HasValue) {
            return;
        }
        
        // Apply debounce - ignore if key was released too recently
        if (ShouldDebounce(key)) {
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Debouncing key up: {Key}", key);
            }
            return;
        }
        
        // Update last event time for this key
        _lastKeyEventTicks[key] = _keyboardTimer.ElapsedTicks;
        
        // Check if this is the key that was being held for typematic repeat
        if (_currentlyHeldKey.Key == key) {
            _currentlyHeldKey = KeyboardEventArgs.None;
            _typematicActive = false;
        }
        
        // Always enqueue key-up events (if there's space)
        if (_keyboardBuffer.Count < MaxBufferSize) {
            _keyboardBuffer.Enqueue(e);
            _hasUnreadKeys = true;
            
            if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                _loggerService.Debug("Key up processed: Key={Key}, ScanCode={ScanCode:X2}", 
                    key, e.ScanCode.Value);
            }
            
            // Trigger interrupt for key release
            _dualPic.ProcessInterruptRequest(1);
        }
    }
    
    /// <summary>
    /// Create a KeyboardEventArgs object from a key and pressed state
    /// </summary>
    private KeyboardEventArgs CreateKeyboardEventArgs(Key key, bool isPressed) {
        // Convert Key enum to scan code and ASCII
        byte? scanCode = KeyToScanCode(key);
        byte? asciiCode = KeyToAsciiCode(key);
        
        return new KeyboardEventArgs(key, isPressed, scanCode, asciiCode);
    }
    
    /// <summary>
    /// Convert a Key to a scan code
    /// </summary>
    private byte? KeyToScanCode(Key key) {
        // Implementation would map Key enum values to scan codes
        // This is a simplified example
        return (byte)key;
    }
    
    /// <summary>
    /// Convert a Key to an ASCII code
    /// </summary>
    private byte? KeyToAsciiCode(Key key) {
        // Implementation would map Key enum values to ASCII codes
        // This is a simplified example
        if (key >= Key.A && key <= Key.Z) {
            return (byte)((byte)key + 32); // lowercase ASCII
        }
        return null;
    }
    
    /// <summary>
    /// Determines if a key event should be ignored due to debouncing
    /// </summary>
    /// <param name="key">The key to check</param>
    /// <returns>True if the event should be ignored, false otherwise</returns>
    private bool ShouldDebounce(Key key) {
        if (_lastKeyEventTicks.TryGetValue(key, out long lastTimeTicks)) {
            long elapsedTicks = _keyboardTimer.ElapsedTicks - lastTimeTicks;
            return elapsedTicks < _debounceThresholdTicks;
        }
        return false;
    }
    
    /// <summary>
    /// Handle typematic repeat functionality
    /// </summary>
    private void UpdateTypematic() {
        // Do nothing if no key is being held or if we don't have a valid scan code
        if (_currentlyHeldKey.Equals(KeyboardEventArgs.None) || !_currentlyHeldKey.ScanCode.HasValue) {
            return;
        }
        
        long currentTicks = _keyboardTimer.ElapsedTicks;
        long elapsedTicks = currentTicks - _lastKeyPressTimeTicks;
        
        if (!_typematicActive) {
            // Check if we've passed the initial delay
            if (elapsedTicks >= _typematicDelay.TotalSeconds * Stopwatch.Frequency) {
                _typematicActive = true;
                _lastKeyPressTimeTicks = currentTicks; // Reset the timer for rate calculation
            }
        } else {
            // We're in active repeat mode, check if it's time for a repeat
            if (elapsedTicks >= _typematicRate.TotalSeconds * Stopwatch.Frequency) {
                // Add a repeat of the key to the buffer if there's space
                if (_keyboardBuffer.Count < MaxBufferSize) {
                    _keyboardBuffer.Enqueue(_currentlyHeldKey);
                    _hasUnreadKeys = true;
                    
                    if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                        _loggerService.Debug("Typematic repeat: Key={Key}, ScanCode={ScanCode:X2}", 
                            _currentlyHeldKey.Key, _currentlyHeldKey.ScanCode.Value);
                    }
                    
                    // Trigger interrupt for repeated key
                    _dualPic.ProcessInterruptRequest(1);
                }
                _lastKeyPressTimeTicks = currentTicks; // Reset timer for next repeat
            }
        }
    }

    /// <summary>
    /// Gets the next keyboard event from the buffer or returns an empty event if buffer is empty.
    /// </summary>
    public KeyboardEventArgs GetNextKeyboardEvent() {
        if (_keyboardBuffer.Count > 0) {
            KeyboardEventArgs evt = _keyboardBuffer.Dequeue();
            
            // If buffer is now empty, update status flag
            if (_keyboardBuffer.Count == 0) {
                _hasUnreadKeys = false;
            }
            
            return evt;
        }
        
        // No events in the queue
        _hasUnreadKeys = false;
        return KeyboardEventArgs.None;
    }

    /// <summary>
    /// Gets the next keyboard scan code from the buffer.
    /// </summary>
    private byte? ReadNextScanCode() {
        if (_keyboardBuffer.Count == 0) {
            return null;
        }
        
        KeyboardEventArgs keyEvent = _keyboardBuffer.Dequeue();
        
        // If this was the last key, update the status
        if (_keyboardBuffer.Count == 0) {
            _hasUnreadKeys = false;
        }
        
        return keyEvent.ScanCode;
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data:
                byte? scancode = ReadNextScanCode();
                return scancode ?? 0;
                
            case KeyboardPorts.StatusRegister:
                byte status = SystemTestStatusMask | KeyboardEnableStatusMask;
                // Set output buffer full bit if we have keys in the buffer
                if (_hasUnreadKeys) {
                    status |= OutputBufferFullMask;
                }
                return status;
                
            default:
                return base.ReadByte(port);
        }
    }

    /// <inheritdoc />
    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case KeyboardPorts.Data:
                _a20Gate.IsEnabled = Command switch {
                    KeyboardCommand.SetOutputPort => (value & 2) > 0,
                    KeyboardCommand.EnableA20Gate => false,
                    KeyboardCommand.DisableA20Gate => true,
                    _ => _a20Gate.IsEnabled
                };
                Command = KeyboardCommand.None;
                break;
            case KeyboardPorts.Command:
                if (Enum.IsDefined(typeof(KeyboardCommand), value)) {
                    Command = (KeyboardCommand)value;
                } else {
                    throw new NotImplementedException("Keyboard command not recognized or not implemented.");
                }
                break;
            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    /// Sets the typematic delay and rate parameters
    /// </summary>
    /// <param name="delayMs">Delay in milliseconds before repeating starts</param>
    /// <param name="rateMs">Rate in milliseconds between repeats</param>
    public void SetTypematicParameters(int delayMs, int rateMs) {
        _typematicDelay = TimeSpan.FromMilliseconds(delayMs);
        _typematicRate = TimeSpan.FromMilliseconds(rateMs);
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.StatusRegister, this);
    }
    
    /// <summary>
    /// Class to hold the current state of all keyboard keys
    /// </summary>
    private class KeyboardState {
        private readonly Dictionary<Key, bool> _keyStates = new Dictionary<Key, bool>();
        
        /// <summary>
        /// Check if a key is currently pressed
        /// </summary>
        public bool IsKeyPressed(Key key) {
            return _keyStates.TryGetValue(key, out bool pressed) && pressed;
        }
        
        /// <summary>
        /// Set the pressed state of a key
        /// </summary>
        public void SetKeyState(Key key, bool isPressed) {
            _keyStates[key] = isPressed;
        }
    }
}