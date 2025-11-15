namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;
using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Diagnostics;

[DebuggerDisplay("PS2Keyboard Set={_codeSet} Scanning={_isScanning} Buf={_bufferNumUsed}/{BufferSize} Overflowed={_bufferOverflowed} Repeat={_repeat.Key} WaitMs={_repeat.WaitMs}")]
public class PS2Keyboard : IDisposable {
    private readonly Intel8042Controller _controller;
    private readonly ILoggerService _loggerService;
    private readonly KeyboardScancodeConverter _scancodeConverter = new();
    private readonly State _cpuState;
    private readonly DualPic _dualPic;
    private readonly IGuiKeyboardEvents? _gui;
    
    // Handler references for PIC event management
    private readonly EmulatedTimeEventHandler _service1msHandler;
    private readonly EmulatedTimeEventHandler _ledsAllOnExpireHandler;

    // Internal keyboard scancode buffer - mirrors DOSBox implementation
    private const int BufferSize = 8; // in scancodes
    private readonly List<byte>[] _buffer = new List<byte>[BufferSize];
    private bool _bufferOverflowed = false;
    private int _bufferStartIdx = 0;
    private int _bufferNumUsed = 0;

    // Key repetition (ms-based), DOSBox-style
    private struct RepeatData {
        public KbdKey Key;     // key which went typematic
        public int WaitMs;     // ms until next event
        public int PauseMs;    // initial delay (ms)
        public int RateMs;     // repeat rate (ms)
    }
    private RepeatData _repeat = new() { Key = KbdKey.None, WaitMs = 0, PauseMs = 500, RateMs = 33 };

    // Set3-specific code info
    private class Set3CodeInfoEntry {
        public bool IsEnabledTypematic = true;
        public bool IsEnabledMake = true;
        public bool IsEnabledBreak = true;
    }
    private readonly Dictionary<byte, Set3CodeInfoEntry> _set3CodeInfo = new();

    // State of keyboard LEDs, as requested via keyboard controller
    private byte _ledState = 0;
    // If true, all LEDs are on due to keyboard reset (managed by separate PIC event handler)
    private bool _ledsAllOn = false;
    // If false, keyboard does not push keycodes to the controller
    private bool _isScanning = true;

    private byte _codeSet = 1;

    // Command currently being executed, waiting for parameter
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    /// <summary>
    /// Defines well-known PS/2 keyboard response/data bytes sent to the controller (port 0x60).
    /// </summary>
    /// <remarks>
    /// Reference:
    /// - https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html
    /// </remarks>
    public enum DataByte : byte {
        /// <summary>
        /// Acknowledge byte sent after most valid commands.
        /// </summary>
        Ack = 0xFA,

        /// <summary>
        /// Request to resend the last command/byte (typically after an error).
        /// </summary>
        Resend = 0xFE,

        /// <summary>
        /// Response to the ECHO command. Note: this is sent without a preceding ACK.
        /// </summary>
        Echo = 0xEE,

        /// <summary>
        /// Basic Assurance Test (BAT) passed.
        /// </summary>
        BatOk = 0xAA,

        /// <summary>
        /// First byte (prefix) of the two-byte keyboard identification sequence returned by the Identify command.
        /// </summary>
        IdentifyPrefix = 0xAB,

        /// <summary>
        /// Second byte of the Identify response for the common MF2 101/102-key keyboard (sequence: 0xAB, 0x83).
        /// </summary>
        IdentifyMf2 = 0x83,

        /// <summary>
        /// Second byte of the Identify response for many space saver keyboards (sequence: 0xAB, 0x84).
        /// </summary>
        IdentifySpaceSaver = 0x84,

        /// <summary>
        /// Second byte of the Identify response for many 122-key keyboards (sequence: 0xAB, 0x86).
        /// </summary>
        Identify122Key = 0x86
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PS2Keyboard"/> class.
    /// </summary>
    /// <param name="controller">The keyboard controller.</param>
    /// <param name="cpuState">The CPU state for cycle-based timing.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="dualPic">The PIC event queue for scheduling keyboard events.</param>
    /// <param name="gui">Optional GUI interface for keyboard events.</param>
    public PS2Keyboard(Intel8042Controller controller, State cpuState,
        ILoggerService loggerService, DualPic dualPic,
        IGuiKeyboardEvents? gui = null) {
        _controller = controller;
        _cpuState = cpuState;
        _loggerService = loggerService;
        _dualPic = dualPic;
        
        // Initialize handler references
        _service1msHandler = TypematicTickHandler;
        _ledsAllOnExpireHandler = LedsAllOnExpireHandler;

        // Schedule first 1ms periodic service (typematic + LED timeout)
        _dualPic.AddEvent(_service1msHandler, 0.001);

        KeyboardReset(isStartup: true);

        _gui = gui;
        if (_gui is not null) {
            _gui.KeyDown += OnKeyEvent;
            _gui.KeyUp += OnKeyEvent;
        }
    }

    /// <summary>
    /// Unsubscribes from GUI events to prevent memory leaks.
    /// </summary>
    public void Dispose() {
        if (_gui is not null) {
            _gui.KeyDown -= OnKeyEvent;
            _gui.KeyUp -= OnKeyEvent;
        }
    }

    // ***************************************************************************
    // Helper routines to log various warnings
    // ***************************************************************************

    private void WarnResend() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Resend command not implemented");
        }
    }

    private void WarnUnknownCommand(KeyboardCommand command) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Unknown command 0x{Command:X2}", (byte)command);
        }
    }

    private void WarnUnknownScancodeSet() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Guest requested unknown scancode set");
        }
    }

    // ***************************************************************************
    // keyboard buffer support
    // ***************************************************************************

    private void MaybeTransferBuffer() {
        if (_bufferNumUsed == 0 || !_controller.IsReadyForKbdFrame) {
            return;
        }

        _controller.AddKbdFrame(_buffer[_bufferStartIdx]);

        --_bufferNumUsed;
        _bufferStartIdx = (_bufferStartIdx + 1) % BufferSize;
    }

    private void BufferAdd(List<byte>? scanCode) {
        // Ignore unsupported keys, drop everything if buffer overflowed
        if (scanCode is null or { Count: 0 } || _bufferOverflowed) {
            return;
        }

        // If buffer got overflowed, drop everything until
        // the controllers queue gets free for the keyboard
        if (_bufferNumUsed == BufferSize) {
            _bufferNumUsed = 0;
            _bufferOverflowed = true;
            return;
        }

        // We can safely add a scancode to the buffer
        int idx = (_bufferStartIdx + _bufferNumUsed++) % BufferSize;
        _buffer[idx] = [.. scanCode];

        // If possible, transfer the scancode to keyboard controller
        MaybeTransferBuffer();
    }

    // ***************************************************************************
    // 1ms service loop (typematic + LED timeout)
    // ***************************************************************************

    private void TypematicTickHandler(uint _) {
        if (_repeat.WaitMs > 0) {
            _repeat.WaitMs--;
        }

        if (_repeat.Key == KbdKey.None) {
            return;
        }

        // Check if buffers are free
        if (_bufferNumUsed > 0 || !_controller.IsReadyForKbdFrame) {
            _repeat.WaitMs = 1;
            return;
        }

        // Simulate key press
        AddKey(_repeat.Key, isPressed: true);
    }
    
    private void LedsAllOnExpireHandler(uint _) {
        _ledsAllOn = false;
        MaybeNotifyLedState();
    }

    // ***************************************************************************
    // Key repetition
    // ***************************************************************************

    private void TypematicUpdate(KbdKey keyType, bool isPressed) {
        if (keyType is KbdKey.Pause or KbdKey.PrintScreen) {
            // Key is excluded from being repeated
        } else if (isPressed) {
            _repeat.WaitMs = _repeat.Key == keyType ? _repeat.RateMs : _repeat.PauseMs;
            _repeat.Key = keyType;
        } else if (_repeat.Key == keyType) {
            // Currently repeated key being released
            _repeat.Key = KbdKey.None;
            _repeat.WaitMs = 0;
        }
    }

    private void TypematicUpdateSet3(KbdKey keyType, List<byte>? scanCode, bool isPressed) {
        // Ignore keys not supported in set 3
        if (scanCode is null or { Count: 0 }) {
            return;
        }

        // Ignore keys for which typematic behavior was disabled
        byte code = scanCode[^1]; // last byte
        if (!GetSet3CodeInfo(code).IsEnabledTypematic) {
            return;
        }

        // For all the other keys, follow usual behavior
        TypematicUpdate(keyType, isPressed);
    }

    // ***************************************************************************
    // Keyboard microcontroller high-level emulation
    // ***************************************************************************

    private void MaybeNotifyLedState() {
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("KEYBOARD: LED state: {LedState:X2}", GetLedState());
        }
    }

    private void ClearBuffer() {
        _bufferStartIdx = 0;
        _bufferNumUsed = 0;
        _bufferOverflowed = false;

        _repeat.Key = KbdKey.None;
        _repeat.WaitMs = 0;
    }

    private bool SetCodeSet(byte requestedSet) {
        if (requestedSet is < 1 or > 3) {
            WarnUnknownScancodeSet();
            return false;
        }

        byte oldSet = _codeSet;
        _codeSet = requestedSet;

        if (_codeSet != oldSet && _loggerService.IsEnabled(LogEventLevel.Information)) {
            _loggerService.Information("KEYBOARD: Using scancode set #{CodeSet}", _codeSet);
        }

        ClearBuffer();
        return true;
    }

    private void SetTypeRate(byte value) {
        // DOSBox tables in ms
        int[] pauseTable = { 250, 500, 750, 1000 };
        int[] rateTable = {
             33,  37,  42,  46,  50,  54,  58,  63,
             67,  75,  83,  92, 100, 109, 118, 125,
            133, 149, 167, 182, 200, 217, 233, 250,
            270, 303, 333, 370, 400, 435, 476, 500
        };

        int pauseIdx = (value & 0b0110_0000) >> 5;
        int rateIdx = (value & 0b0001_1111);

        _repeat.PauseMs = pauseTable[pauseIdx];
        _repeat.RateMs = rateTable[rateIdx];
    }

    private void SetDefaults() {
        _repeat.Key = KbdKey.None;
        _repeat.PauseMs = 500;
        _repeat.RateMs = 33;
        _repeat.WaitMs = 0;

        foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
            entry.IsEnabledMake = true;
            entry.IsEnabledBreak = true;
            entry.IsEnabledTypematic = true;
        }

        SetCodeSet(1);
    }

    private void KeyboardReset(bool isStartup = false) {
        SetDefaults();
        ClearBuffer();

        _isScanning = true;

        // Flash all the LEDs - DOSBox Staging pattern
        _dualPic.RemoveEvents(_ledsAllOnExpireHandler);
        _ledState = 0;
        _ledsAllOn = !isStartup;
        if (_ledsAllOn) {
            // To commemorate how evil the whole keyboard subsystem is,
            // let's set blink expiration time to 666 milliseconds
            const double expireTimeMs = 666.0;
            _dualPic.AddEvent(_ledsAllOnExpireHandler, expireTimeMs);
        }
        MaybeNotifyLedState();
    }

    private void ExecuteCommand(KeyboardCommand command) {
        if(_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("KEYBOARD: Command 0x{Command:X2}", (byte)command);
        }

        switch (command) {
            //
            // Commands requiring a parameter
            //
            case KeyboardCommand.SetLeds:          // 0xed
            case KeyboardCommand.SetTypeRate:      // 0xf3
                _controller.AddKbdByte((byte)DataByte.Ack);
                _currentCommand = command;
                break;
            case KeyboardCommand.CodeSet:          // 0xf0
            case KeyboardCommand.Set3KeyTypematic: // 0xfb
            case KeyboardCommand.Set3KeyMakeBreak: // 0xfc
            case KeyboardCommand.Set3KeyMakeOnly:  // 0xfd
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                _currentCommand = command;
                break;
            //
            // No-parameter commands
            //
            case KeyboardCommand.Echo: // 0xee
                // Diagnostic echo, responds without acknowledge
                _controller.AddKbdByte((byte)DataByte.Echo);
                break;
            case KeyboardCommand.Identify: // 0xf2
                // Returns keyboard ID
                // - 0xab, 0x83: typical for multifunction PS/2 keyboards
                // - 0xab, 0x84: many short, space saver keyboards
                // - 0xab, 0x86: many 122-key keyboards
                _controller.AddKbdByte((byte)DataByte.Ack);
                _controller.AddKbdByte((byte)DataByte.IdentifyPrefix);
                _controller.AddKbdByte((byte)DataByte.IdentifyMf2);
                break;
            case KeyboardCommand.ClearEnable: // 0xf4
                // Clear internal buffer, enable scanning
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                _isScanning = true;
                break;
            case KeyboardCommand.DefaultDisable: // 0xf5
                // Restore defaults, disable scanning
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                SetDefaults();
                _isScanning = false;
                break;
            case KeyboardCommand.ResetEnable: // 0xf6
                // Restore defaults, enable scanning
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                SetDefaults();
                _isScanning = true;
                break;
            case KeyboardCommand.Set3AllTypematic: // 0xf7
                // Set scanning type for all the keys,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
                    entry.IsEnabledTypematic = true;
                    entry.IsEnabledMake = false;
                    entry.IsEnabledBreak = false;
                }
                break;
            case KeyboardCommand.Set3AllMakeBreak: // 0xf8
                // Set scanning type for all the keys,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
                    entry.IsEnabledTypematic = false;
                    entry.IsEnabledMake = true;
                    entry.IsEnabledBreak = true;
                }
                break;
            case KeyboardCommand.Set3AllMakeOnly: // 0xf9
                // Set scanning type for all the keys,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
                    entry.IsEnabledTypematic = false;
                    entry.IsEnabledMake = true;
                    entry.IsEnabledBreak = false;
                }
                break;
            case KeyboardCommand.Set3AllTypeMakeBreak: // 0xfa
                // Set scanning type for all the keys,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
                    entry.IsEnabledTypematic = true;
                    entry.IsEnabledMake = true;
                    entry.IsEnabledBreak = true;
                }
                break;
            case KeyboardCommand.Resend: // 0xfe
                // Resend byte, should normally be used on transmission
                // errors - not implemented, as the emulation can
                // also send whole multi-byte scancode at once
                WarnResend();
                // We have to respond, or else the 'In Extremis' game intro
                // (sends 0xfe and 0xaa commands) hangs with a black screen
                _controller.AddKbdByte((byte)DataByte.Ack);
                break;
            case KeyboardCommand.Reset: // 0xff
                // Full keyboard reset and self test
                // 0xaa: passed; 0xfc/0xfd: failed
                _controller.AddKbdByte((byte)DataByte.Ack);
                KeyboardReset();
                _controller.AddKbdByte((byte)DataByte.BatOk);
                break;
            //
            // Unknown commands
            //
            default:
                WarnUnknownCommand(command);
                _controller.AddKbdByte((byte)DataByte.Resend);
                break;
        }
    }

    private void ExecuteCommand(KeyboardCommand command, byte param) {
        if(_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("KEYBOARD: Command 0x{Command:X2}, parameter 0x{Param:X2}", (byte)command, param);
        }

        switch (command) {
            case KeyboardCommand.SetLeds: // 0xed
                // Set keyboard LEDs according to bitfield
                _controller.AddKbdByte((byte)DataByte.Ack);
                _ledState = param;
                MaybeNotifyLedState();
                break;
            case KeyboardCommand.CodeSet: // 0xf0
                // Query or change the scancode set
                if (param != 0) {
                    // Change scancode set
                    if (SetCodeSet(param)) {
                        _controller.AddKbdByte((byte)DataByte.Ack);
                    } else {
                        _currentCommand = command;
                        _controller.AddKbdByte((byte)DataByte.Resend);
                    }
                } else {
                    // Query current scancode set
                    _controller.AddKbdByte((byte)DataByte.Ack);
                    _controller.AddKbdByte(_codeSet);
                }
                break;
            case KeyboardCommand.SetTypeRate: // 0xf3
                // Sets typematic rate/delay
                _controller.AddKbdByte((byte)DataByte.Ack);
                SetTypeRate(param);
                break;
            case KeyboardCommand.Set3KeyTypematic: // 0xfb
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                GetSet3CodeInfo(param).IsEnabledTypematic = true;
                GetSet3CodeInfo(param).IsEnabledMake = false;
                GetSet3CodeInfo(param).IsEnabledBreak = false;
                break;
            case KeyboardCommand.Set3KeyMakeBreak: // 0xfc
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                GetSet3CodeInfo(param).IsEnabledTypematic = false;
                GetSet3CodeInfo(param).IsEnabledMake = true;
                GetSet3CodeInfo(param).IsEnabledBreak = true;
                break;
            case KeyboardCommand.Set3KeyMakeOnly: // 0xfd
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte((byte)DataByte.Ack);
                ClearBuffer();
                GetSet3CodeInfo(param).IsEnabledTypematic = false;
                GetSet3CodeInfo(param).IsEnabledMake = true;
                GetSet3CodeInfo(param).IsEnabledBreak = false;
                break;
            default:
                // If we are here, than either this function
                // was wrongly called or it is incomplete
                break;
        }
    }

    private Set3CodeInfoEntry GetSet3CodeInfo(byte scancode) {
        if (!_set3CodeInfo.TryGetValue(scancode, out Set3CodeInfoEntry? entry)) {
            entry = new Set3CodeInfoEntry();
            _set3CodeInfo[scancode] = entry;
        }
        return entry;
    }

    // ***************************************************************************
    // External interfaces
    // ***************************************************************************

    /// <summary>
    /// Handles keyboard key events from the UI.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The keyboard event arguments.</param>
    public void OnKeyEvent(object? sender, KeyboardEventArgs e) {
        KbdKey keyType = _scancodeConverter.ConvertToKbdKey(e.Key);
        AddKey(keyType, e.IsPressed);
    }

    /// <summary>
    /// Handles writes to the keyboard port from the controller.
    /// </summary>
    /// <param name="value">The value written to the port.</param>
    public void PortWrite(byte value) {
        // Highest bit set usually means a command
        bool isCommand = (value & 0x80) != 0 &&
                        _currentCommand != KeyboardCommand.Set3KeyTypematic &&
                        _currentCommand != KeyboardCommand.Set3KeyMakeBreak &&
                        _currentCommand != KeyboardCommand.Set3KeyMakeOnly;

        if (isCommand) {
            // Terminate previous command
            _currentCommand = KeyboardCommand.None;
        }

        KeyboardCommand command = _currentCommand;
        if (command != KeyboardCommand.None) {
            // Continue execution of previous command
            _currentCommand = KeyboardCommand.None;
            ExecuteCommand(command, value);
        } else if (isCommand) {
            ExecuteCommand((KeyboardCommand)value);
        }
    }

    /// <summary>
    /// Called when the controller is ready to accept keyboard frames.
    /// </summary>
    internal void NotifyReadyForFrame() {
        // Since the guest software seems to be reacting on keys again,
        // clear the buffer overflow flag, do not ignore keys any more
        _bufferOverflowed = false;
        MaybeTransferBuffer();
    }

    /// <summary>
    /// Adds a key event to be processed by the keyboard.
    /// </summary>
    /// <param name="keyType">The key type.</param>
    /// <param name="isPressed">Whether the key is pressed or released.</param>
    public void AddKey(KbdKey keyType, bool isPressed) {
        if (!_isScanning) {
            return;
        }

        List<byte> scanCode = new();

        switch (_codeSet) {
            case 1:
                scanCode = _scancodeConverter.GetScanCode1(keyType, isPressed);
                TypematicUpdate(keyType, isPressed);
                break;
            case 2:
                scanCode = _scancodeConverter.GetScanCode2(keyType, isPressed);
                TypematicUpdate(keyType, isPressed);
                break;
            case 3:
                scanCode = _scancodeConverter.GetScanCode3(keyType, isPressed);
                TypematicUpdateSet3(keyType, scanCode, isPressed);
                break;
            default:
                break;
        }

        BufferAdd(scanCode);
    }

    /// <summary>
    /// Gets the current LED state.
    /// </summary>
    /// <returns>The LED state byte.</returns>
    public byte GetLedState() {
        // We support only 3 leds
        return (byte)((_ledsAllOn ? 0xff : _ledState) & 0b0000_0111);
    }

    /// <summary>
    /// Initializes the keyboard component.
    /// </summary>
    public void Initialize() {
        const bool isStartup = true;
        KeyboardReset(isStartup);
        SetCodeSet(1);
    }
}