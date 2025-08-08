namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Basic implementation of a keyboard
/// </summary>
public sealed class Keyboard : DefaultIOPortHandler {
    private readonly IGui? _gui;
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;
    private readonly KeyboardBuffer _keyboardBuffer;
    private readonly Dictionary<Key, KeyState> _pressedKeys = new();
    private readonly Stopwatch _keyRepeatTimer = new Stopwatch();

    // Typematic rate control
    private byte _typematicRate = 0x0B; // Default: ~10.9 chars/sec
    private byte _typematicDelay = 0x01; // Default: 500ms delay

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
    /// Status bit indicating output buffer is full (data available)
    /// </summary>
    public const byte OutputBufferFullMask = 1 << 0;

    /// <summary>
    /// Status bit indicating input buffer is full (controller busy)
    /// </summary>
    public const byte InputBufferFullMask = 1 << 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Keyboard"/> class.
    /// </summary>
    /// <param name="state">The CPU state.</param>
    /// <param name="ioPortDispatcher">The class that is responsible for dispatching ports reads and writes to classes that respond to them.</param>
    /// <param name="a20Gate">The class that controls whether the CPU's 20th address line is enabled.</param>
    /// <param name="dualPic">The two programmable interrupt controllers.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="gui">The graphical user interface. Is null in headless mode.</param>
    /// <param name="failOnUnhandledPort">Whether we throw an exception when an I/O port wasn't handled.</param>
    public Keyboard(State state, IOPortDispatcher ioPortDispatcher, A20Gate a20Gate, DualPic dualPic,
        ILoggerService loggerService, IGui? gui, bool failOnUnhandledPort)
        : base(state, failOnUnhandledPort, loggerService) {
        _gui = gui;
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        _keyboardBuffer = new KeyboardBuffer(loggerService, dualPic);
        
        if (_gui is not null) {
            _gui.KeyUp += OnKeyUp;
            _gui.KeyDown += OnKeyDown;
        }
        InitPortHandlers(ioPortDispatcher);
        _keyRepeatTimer.Start();
    }

    private void OnKeyDown(object? sender, KeyboardEventArgs e) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Keyboard key down event: {KeyboardKeyDownEvent}", e);
        }

        // If key is already pressed, check if we should ignore this repeat
        if (e.ScanCode.HasValue && _pressedKeys.TryGetValue(e.Key, out KeyState? keyState)) {
            long currentTime = _keyRepeatTimer.ElapsedMilliseconds;
            long timeSincePress = currentTime - keyState.PressTime;
            long typematicDelayMs = (_typematicDelay + 1) * 250;

            // Calculate typematic rate in ms
            double rateHz = (8.0 + (_typematicRate & 0x1F)) * Math.Pow(2, -(((_typematicRate >> 5) & 0x03)));
            long repeatIntervalMs = (long)(1000 / rateHz);

            // If within initial delay OR if not enough time since last repeat, ignore
            if (timeSincePress < typematicDelayMs ||
                (timeSincePress >= typematicDelayMs &&
                 currentTime - keyState.LastRepeatTime < repeatIntervalMs)) {
                return;
            }

            // Update last repeat time
            keyState.LastRepeatTime = currentTime;
        }

        // Add to keyboard buffer
        if (e.ScanCode.HasValue) {
            _keyboardBuffer.Add(e.ScanCode.Value);
            
            // Record key press for typematic tracking
            if (!_pressedKeys.ContainsKey(e.Key)) {
                _pressedKeys[e.Key] = new KeyState(e.ScanCode.Value, _keyRepeatTimer.ElapsedMilliseconds);
            }
        }
    }

    private void OnKeyUp(object? sender, KeyboardEventArgs e) {
        if (_loggerService.IsEnabled(LogEventLevel.Verbose)) {
            _loggerService.Verbose("Keyboard key up event: {KeyboardKeyUpEvent}", e);
        }

        // Add key-up scancode to buffer (scancode + 0x80)
        if (e.ScanCode.HasValue) {
            _keyboardBuffer.Add((byte)(e.ScanCode.Value | 0x80));
            
            // Remove from pressed keys
            _pressedKeys.Remove(e.Key);
        }
    }

    /// <summary>
    /// Gets whether there are pending keyboard events in the buffer
    /// </summary>
    private bool HasPendingEvents => _keyboardBuffer.HasData;

    /// <summary>
    /// Gets the next keyboard event from the buffer without removing it
    /// </summary>
    private byte? PeekScanCode() {
        return _keyboardBuffer.Peek();
    }

    /// <summary>
    /// Gets and removes the next keyboard event from the buffer
    /// </summary>
    internal byte DequeueEvent() {
        return _keyboardBuffer.Dequeue();
    }

    /// <inheritdoc/>   
    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data:
                byte? scancode = PeekScanCode();
                if (scancode.HasValue) {
                    // Remove from queue only after successful read
                    return scancode.Value;
                }
                return 0;

            case KeyboardPorts.StatusRegister:
                // Return status: keyboard not locked, self-test completed, buffer status
                byte status = SystemTestStatusMask | KeyboardEnableStatusMask;
                if (HasPendingEvents) {
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
                switch (Command) {
                    case KeyboardCommand.SetOutputPort:
                        _a20Gate.IsEnabled = (value & 2) > 0;
                        Command = KeyboardCommand.None;
                        break;

                    case KeyboardCommand.SetTypematicRateAndDelay:
                        // Bits 5-7: Delay before typematic repeat
                        // 00: 250ms, 01: 500ms, 10: 750ms, 11: 1000ms
                        _typematicDelay = (byte)((value >> 5) & 0x03);

                        // Bits 0-4: Typematic rate (chars/sec)
                        // Formula: Rate = (8 + B) * 2^(-A) where A = bits 5-6, B = bits 0-4
                        _typematicRate = (byte)(value & 0x1F);

                        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
                            // Calculate human-readable values for logging
                            int delayMs = (_typematicDelay + 1) * 250;
                            double rateHz = (8.0 + (_typematicRate & 0x1F)) *
                                Math.Pow(2, -(((_typematicRate >> 5) & 0x03)));

                            _loggerService.Debug("Keyboard typematic set: delay={DelayMs}ms, rate={RateHz:F1}Hz",
                                delayMs, rateHz);
                        }

                        // Respond with ACK (0xFA)
                        _keyboardBuffer.Add(0xFA);
                        _dualPic.ProcessInterruptRequest(1);
                        Command = KeyboardCommand.None;
                        break;

                    case KeyboardCommand.EnableA20Gate:
                        _a20Gate.IsEnabled = false;
                        Command = KeyboardCommand.None;
                        break;

                    case KeyboardCommand.DisableA20Gate:
                        _a20Gate.IsEnabled = true;
                        Command = KeyboardCommand.None;
                        break;

                    default:
                        Command = KeyboardCommand.None;
                        break;
                }
                break;

            case KeyboardPorts.Command:
                if (Enum.IsDefined(typeof(KeyboardCommand), value)) {
                    Command = (KeyboardCommand)value;

                    // Handle specific commands immediately if they don't need parameters
                    switch (Command) {
                        case KeyboardCommand.Reset:
                            // Respond with self-test passed (0xAA)
                            _keyboardBuffer.Add(0xAA);
                            _dualPic.ProcessInterruptRequest(1);
                            Command = KeyboardCommand.None;
                            break;

                        case KeyboardCommand.ReadControllerRamByte:
                            // Return default controller byte 0x00
                            _keyboardBuffer.Add(0x00);
                            _dualPic.ProcessInterruptRequest(1);
                            Command = KeyboardCommand.None;
                            break;
                    }
                } else {
                    throw new NotImplementedException("Keyboard command not recognized or not implemented.");
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Command, this);
    }

    /// <summary>
    /// Class to track key press state and timing for typematic repeat
    /// </summary>
    private class KeyState {
        public byte ScanCode { get; }
        public long PressTime { get; }
        public long LastRepeatTime { get; set; }

        public KeyState(byte scanCode, long pressTime) {
            ScanCode = scanCode;
            PressTime = pressTime;
            LastRepeatTime = pressTime;
        }
    }
}