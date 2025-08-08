namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implements a fixed-size circular buffer for keyboard scancodes.
/// </summary>
public class KeyboardBuffer {
    private const int BufferSize = 8;
    private readonly byte[][] _buffer = new byte[BufferSize][];
    private int _startIndex = 0;
    private int _numUsed = 0;
    private bool _overflowed = false;
    private readonly ILoggerService _loggerService;
    private readonly DualPic _dualPic;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyboardBuffer"/> class.
    /// </summary>
    /// <param name="loggerService">Logger service for diagnostic messages.</param>
    /// <param name="dualPic">PIC for triggering interrupts.</param>
    public KeyboardBuffer(ILoggerService loggerService, DualPic dualPic) {
        _loggerService = loggerService;
        _dualPic = dualPic;
    }

    /// <summary>
    /// Gets whether the buffer has data available.
    /// </summary>
    public bool HasData => _numUsed > 0;

    /// <summary>
    /// Gets the next scancode from the buffer without removing it.
    /// </summary>
    /// <returns>The next scancode or null if buffer is empty.</returns>
    public byte? Peek() {
        if (_numUsed == 0) {
            return null;
        }
        return _buffer[_startIndex][0];
    }

    /// <summary>
    /// Gets and removes the next scancode from the buffer.
    /// </summary>
    /// <returns>The next scancode or 0 if buffer is empty.</returns>
    public byte Dequeue() {
        if (_numUsed == 0) {
            return 0;
        }

        byte result = _buffer[_startIndex][0];
        _startIndex = (_startIndex + 1) % BufferSize;
        _numUsed--;

        // Reset overflow flag if the buffer is completely emptied
        if (_numUsed == 0) {
            _overflowed = false;
        }

        return result;
    }

    /// <summary>
    /// Adds a scancode to the buffer.
    /// </summary>
    /// <param name="scancode">Scancode to add.</param>
    /// <returns>True if scancode was successfully added.</returns>
    public bool Add(byte scancode) {
        return Add(new byte[] { scancode });
    }

    /// <summary>
    /// Adds a multi-byte scancode to the buffer.
    /// </summary>
    /// <param name="scancode">Array of bytes representing a scancode.</param>
    /// <returns>True if scancode was successfully added.</returns>
    public bool Add(byte[] scancode) {
        if (scancode == null || scancode.Length == 0 || _overflowed) {
            return false;
        }

        // Buffer overflow check
        if (_numUsed == BufferSize) {
            _loggerService.Warning("Keyboard buffer overflow");
            _numUsed = 0;
            _overflowed = true;
            return false;
        }

        int index = (_startIndex + _numUsed++) % BufferSize;
        _buffer[index] = scancode;
        
        _dualPic.ProcessInterruptRequest(1);
        return true;
    }

    /// <summary>
    /// Clears the buffer completely.
    /// </summary>
    public void Clear() {
        _startIndex = 0;
        _numUsed = 0;
        _overflowed = false;
    }
}
