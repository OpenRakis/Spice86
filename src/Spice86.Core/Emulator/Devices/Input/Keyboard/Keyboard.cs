namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

/// <summary>
/// Implementation of a PC/AT keyboard controller
/// </summary>
public sealed class Keyboard : DefaultIOPortHandler {
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;

    // Buffer constants and variables, similar to DOSBox implementation
    private const int KeyboardBufferSize = 16; // Buffer size in scancodes
    private readonly byte[] _buffer = new byte[KeyboardBufferSize];
    private bool _bufferOverflowed = false;
    private int _bufferStartIndex = 0;
    private int _bufferNumUsed = 0;
    private bool _isKeyboardDisabled;

    // Tracks the current scancode that's available for reading
    private byte _currentScanCode = 0;
    // Indicates whether there is data available to be read
    private bool _hasDataAvailable = false;

    /// <summary>
    /// The current keyboard command, such as 'Perform self-test' (0xAA)
    /// </summary>
    public KeyboardCommand Command { get; private set; } = KeyboardCommand.None;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte SystemTestStatusMask = 1 << 2;

    /// <summary>
    /// Part of the value sent when the CPU reads the status register.
    /// </summary>
    public const byte KeyboardEnableStatusMask = 1 << 4;

    /// <summary>
    /// Output buffer full flag (bit 0)
    /// </summary>
    public const byte OutputBufferFullMask = 1 << 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keyboard"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="a20Gate">The class that controls whether the CPU's 20th address line is enabled.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public Keyboard(State state, IOPortDispatcher ioPortDispatcher,
        A20Gate a20Gate, DualPic dualPic, ILoggerService loggerService, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        InitPortHandlers(ioPortDispatcher);
    }

    /// <summary>
    /// Adds a key event to the keyboard buffer and triggers an interrupt if needed
    /// </summary>
    /// <param name="e">The keyboard event arguments</param>
    public void AddKeyToBuffer(KeyboardEventArgs e) {
        // Don't process keyboard events when disabled
        if (_isKeyboardDisabled) {
            return;
        }

        // Only process events with valid scan codes
        if (!e.ScanCode.HasValue) {
            return;
        }

        byte scanCode = e.ScanCode.Value;

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Adding to keyboard buffer: {KeyboardEvent}, Scan Code: 0x{ScanCode:X2}", e, scanCode);
        }

        // Add the scan code to the buffer
        AddScanCodeToBuffer(scanCode);

        // Generate interrupt if we have keys in buffer
        if (HasScanCode()) {
            _dualPic.ProcessInterruptRequest(1);
        }
    }

    /// <summary>
    /// Adds a scancode to the keyboard buffer
    /// </summary>
    /// <param name="scanCode">The scancode to add</param>
    private void AddScanCodeToBuffer(byte scanCode) {
        // If buffer overflowed, drop everything until the buffer gets free
        if (_bufferOverflowed) {
            return;
        }

        // If buffer is full, mark as overflowed and return
        if (_bufferNumUsed == KeyboardBufferSize) {
            _bufferNumUsed = 0;
            _bufferOverflowed = true;
            return;
        }

        // We can safely add a scancode to the buffer
        int index = (_bufferStartIndex + _bufferNumUsed++) % KeyboardBufferSize;
        _buffer[index] = scanCode;

        // Make the first scancode available if no data is currently available
        if (!_hasDataAvailable && _bufferNumUsed > 0) {
            MakeNextScanCodeAvailable();
        }
    }

    /// <summary>
    /// Makes the next scancode from the buffer available for reading
    /// without removing it from the buffer
    /// </summary>
    private void MakeNextScanCodeAvailable() {
        if (_bufferNumUsed > 0) {
            _currentScanCode = _buffer[_bufferStartIndex];
            _hasDataAvailable = true;
        }
    }

    /// <summary>
    /// Checks if there are scan codes in the keyboard buffer
    /// </summary>
    /// <returns>True if the keyboard buffer has scan codes, false otherwise</returns>
    public bool HasScanCode() {
        return _bufferNumUsed > 0;
    }

    /// <summary>
    /// Gets the next scancode from the buffer
    /// </summary>
    /// <returns>The next scancode or 0 if buffer is empty</returns>
    private byte GetNextScanCode() {
        if (_bufferNumUsed == 0) {
            return 0;
        }

        byte scancode = _buffer[_bufferStartIndex];
        _bufferStartIndex = (_bufferStartIndex + 1) % KeyboardBufferSize;
        _bufferNumUsed--;

        // Buffer is no longer overflowed once we read from it
        _bufferOverflowed = false;

        // Update the current scancode if more data is available
        if (_bufferNumUsed > 0) {
            MakeNextScanCodeAvailable();
        } else {
            _hasDataAvailable = false;
        }

        return scancode;
    }

    /// <summary>
    /// Peeks at the next scancode without removing it from the buffer
    /// </summary>
    /// <returns>The next scancode or 0 if buffer is empty</returns>
    public byte PeekScanCode() {
        if (_bufferNumUsed == 0) {
            return 0;
        }

        return _buffer[_bufferStartIndex];
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data:
                // Return the current scancode but don't immediately advance the buffer
                // This allows both INT9 and direct port reads to get the same scancode
                byte result = _currentScanCode;

                if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                    _loggerService.Verbose("Keyboard data port read: 0x{ScanCode:X2}", result);
                }

                // Mark data as read and advance to next scancode
                _hasDataAvailable = false;
                GetNextScanCode();

                return result;

            case KeyboardPorts.StatusRegister:
                byte status = SystemTestStatusMask | KeyboardEnableStatusMask;

                // Set output buffer full flag if we have keys in the buffer
                if (_hasDataAvailable) {
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
                // If keyboard is disabled, don't process data port writes except commands
                if (_isKeyboardDisabled) {
                    return;
                }

                _a20Gate.IsEnabled = Command switch {
                    KeyboardCommand.SetOutputPort => (value & 2) > 0,
                    KeyboardCommand.EnableA20Gate => true,
                    KeyboardCommand.DisableA20Gate => false,
                    _ => _a20Gate.IsEnabled
                };
                Command = KeyboardCommand.None;
                break;

            case KeyboardPorts.Command:
                // Always process commands, even if keyboard is disabled
                // This allows re-enabling a disabled keyboard
                if (Enum.IsDefined(typeof(KeyboardCommand), value)) {
                    Command = (KeyboardCommand)value;

                    // Handle specific keyboard commands
                    switch (Command) {
                        case KeyboardCommand.DisableKeyboard:
                            _isKeyboardDisabled = true;
                            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                                _loggerService.Verbose("Keyboard disabled via command 0xAD");
                            }
                            break;

                        case KeyboardCommand.EnableKeyboard:
                            _isKeyboardDisabled = false;
                            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                                _loggerService.Verbose("Keyboard enabled via command 0xAE");
                            }
                            break;
                    }
                } else {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("Keyboard command {Command:X2} not recognized or not implemented", value);
                    }
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    /// Clears the keyboard buffer
    /// </summary>
    public void ClearBuffer() {
        _bufferStartIndex = 0;
        _bufferNumUsed = 0;
        _bufferOverflowed = false;
        _hasDataAvailable = false;
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.StatusRegister, this);
    }
}