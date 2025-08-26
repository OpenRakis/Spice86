namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Diagnostics;

/// <summary>
/// Emulates the PS/2 keyboard. <br/>
/// See keyboard.cpp for this file and intel8042.cpp for more in DOSBox Staging.
/// Reference: <br/>
/// - https://wiki.osdev.org/%228042%22_PS/2_Controller <br/>
/// - https://homepages.cwi.nl/~aeb/linux/kbd/scancodes.html <br/>
/// - http://www-ug.eecg.toronto.edu/msl/nios_devices/datasheets/PS2%20Keyboard%20Protocol.htm <br/>
/// - https://k.lse.epita.fr/data/8042.pdf (SMSC KBD43W13 whitepaper) <br/>
/// - https://tvsat.com.pl/PDF/W/W83C42P_win.pdf (Winbond W83C42 whitepaper) <br/>
/// - http://www.os2museum.com/wp/ibm-pcat-8042-keyboard-controller-commands/ <br/>
/// - http://www.os2museum.com/wp/ibm-ps2-model-50-keyboard-controller/ <br/>
/// </summary>
public sealed class PS2Keyboard : DefaultIOPortHandler {
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;

    // Buffer constants and variables, similar to DOSBox implementation
    private const int KeyboardBufferSize = 16; // Buffer size in scancodes
    private readonly byte[] _buffer = new byte[KeyboardBufferSize];
    private bool _bufferOverflowed = false;
    private int _bufferStartIndex = 0;
    private int _bufferCount = 0;
    private bool _isKeyboardDisabled;
    private byte _currentScanCode = 0;
    // Indicates whether there is data available to be read
    private bool _hasDataAvailable = false;

    // Delay timer implementation, or else we get way too many keys
    // in a very short time frame.
    private const double PortDelayMs = 150;
    private readonly Stopwatch _delayTimer = new();

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
    /// Initializes a new instance of the <see cref="PS2Keyboard"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="a20Gate">The class that controls whether the CPU's 20th address line is enabled.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public PS2Keyboard(State state, IOPortDispatcher ioPortDispatcher,
        A20Gate a20Gate, DualPic dualPic, ILoggerService loggerService,
        bool failOnUnhandledPort, IGui? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        InitPortHandlers(ioPortDispatcher);
        _delayTimer.Start();
        if(gui is not null) {
            gui.KeyDown += OnKeyEvent;
            gui.KeyUp += OnKeyEvent;
    }
    }

    private void OnKeyEvent(object? sender, KeyboardEventArgs e) {
        if (e.ScanCode.HasValue && e.ScanCode.Value != 0) {
            AddKeyToBuffer(e);
        }
        // Generate interrupt if we have keys in buffer and data is available
        if (_hasDataAvailable) {
            _dualPic.ProcessInterruptRequest(1);
        }
    }

    private void AddKeyToBuffer(KeyboardEventArgs e) {
        // Only process if delay expired and not disabled
        if (!GetHasDelayExpired(e) || e.ScanCode is null) {
            return;
        }

        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Processing keyboard event: {KeyboardEvent}, Scan Code: 0x{ScanCode:X2}", e, e.ScanCode);
        }

        AddScanCodeToBuffer(e.ScanCode.Value);
    }

    private void AddScanCodeToBuffer(byte scanCode) {
        // If buffer overflowed, drop everything until the buffer gets free.
        // ignore unknown keys.
        if (_bufferOverflowed) {
            return;
        }

        // If buffer is full, mark as overflowed and return
        if (_bufferCount == KeyboardBufferSize) {
            _bufferCount = 0;
            _bufferOverflowed = true;
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("Keyboard buffer overflow");
            }
            return;
        }

        _bufferCount++;
        // We can safely add a scancode to the buffer
        int index = (_bufferStartIndex + _bufferCount) % KeyboardBufferSize;
        _buffer[index] = scanCode;

        // Transfer scancode to output port if no data is currently available
        MaybeTransferBuffer();
    }

    private void MaybeTransferBuffer() {
        // If data is already available or buffer is empty, nothing to do
        if (_bufferCount == 0) {
            return;
        }

        // Make scancode available for reading
        _currentScanCode = _buffer[_bufferStartIndex];
        _hasDataAvailable = true;

        // Start delay timer for next scancode
        _delayTimer.Restart();

        _bufferStartIndex = (_bufferStartIndex + 1) % KeyboardBufferSize;
        _bufferCount--;

        // Buffer is no longer overflowed once we advanced
        _bufferOverflowed = false;
    }

    private bool GetHasDelayExpired(KeyboardEventArgs e) {
        return _delayTimer.ElapsedMilliseconds >= PortDelayMs ||
            e.ScanCode.HasValue && e.ScanCode.Value != _currentScanCode;
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data:

                // Return the current scancode but don't immediately advance the buffer
                byte result = _currentScanCode;

                _hasDataAvailable = false;

                MaybeTransferBuffer();

                return result;

            case KeyboardPorts.StatusRegister:
                byte status = SystemTestStatusMask | KeyboardEnableStatusMask;

                // Set output buffer full flag if we have data available
                if (_bufferCount > 0) {
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
                    KeyboardCommand.WriteOutputPort => (value & 2) > 0,
                    KeyboardCommand.EnableA20 => true,
                    KeyboardCommand.DisableA20 => false,
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
                        case KeyboardCommand.DisablePortKbd:
                            _isKeyboardDisabled = true;
                            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                                _loggerService.Verbose("Keyboard disabled via command 0xAD");
                            }
                            break;

                        case KeyboardCommand.EnablePortKbd:
                            _isKeyboardDisabled = false;
                            if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
                                _loggerService.Verbose("Keyboard enabled via command 0xAE");
                            }
                            break;
                        default:
                            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                                _loggerService.Warning("Keyboard command {Command:X2} not implemented", value);
                            }
                            break;
                    }
                } else {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("Keyboard command {Command:X2} not recognized", value);
                    }
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.StatusRegister, this);
    }
}