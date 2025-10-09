namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// PS/2 keyboard emulation. C# port of DOSBox's keyboard.cpp
/// </summary>
public class PS2Keyboard {
    private readonly Intel8042Controller _controller;
    private readonly ILoggerService _loggerService;
    private readonly KeyboardScancodeConverter _scancodeConverter = new();
    private readonly State _cpuState;

    // Real-time timer and pause-awareness
    private readonly Stopwatch _rt = Stopwatch.StartNew();
    private readonly IPauseHandler? _pauseHandler;
    private long _pauseStartedAtTicks = 0;
    private long _pausedAccumTicks = 0;
    private long NowTicks {
        get {
            var now = _rt.ElapsedTicks;
            if (_pauseHandler is null || !_pauseHandler.IsPaused) {
                return now - _pausedAccumTicks;
            }
            return _pauseStartedAtTicks - _pausedAccumTicks;
        }
    }
    private static long MsToTicks(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

    // Internal keyboard scancode buffer - mirrors DOSBox implementation
    private const int BufferSize = 8; // in scancodes
    private readonly List<byte>[] _buffer = new List<byte>[BufferSize];
    private bool _bufferOverflowed = false;
    private int _bufferStartIdx = 0;
    private int _bufferNumUsed = 0;

    // Key repetition mechanism data - mirrors DOSBox struct
    private struct RepeatData {
        public KbdKey Key;      // key which went typematic
        public long WaitTicks;  // absolute timestamp when to generate next repeat
        public long PauseTicks; // initial delay
        public long RateTicks;  // repeat rate
    }
    private RepeatData _repeat = new() { Key = KbdKey.None, WaitTicks = 0, PauseTicks = MsToTicks(500), RateTicks = MsToTicks(33) };

    // Set3-specific code info - mirrors DOSBox Set3CodeInfoEntry
    private class Set3CodeInfoEntry {
        public bool IsEnabledTypematic = true;
        public bool IsEnabledMake = true;
        public bool IsEnabledBreak = true;
    }
    private readonly Dictionary<byte, Set3CodeInfoEntry> _set3CodeInfo = new();

    // State of keyboard LEDs, as requested via keyboard controller
    private byte _ledState = 0;
    // If true, all LEDs are on due to keyboard reset
    private bool _ledsAllOn = false;
    // LED timeout tracking
    private long _ledTimeoutTicks = 0;
    // If false, keyboard does not push keycodes to the controller
    private bool _isScanning = true;

    private byte _codeSet = 1;

    // Command currently being executed, waiting for parameter
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="PS2Keyboard"/> class.
    /// </summary>
    /// <param name="controller">The keyboard controller.</param>
    /// <param name="cpuState">The CPU state for cycle-based timing.</param>
    /// <param name="pauseHandler">The emulation pause/continue notification system.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="guiKeyboardEvents">Optional GUI keyboard events interface.</param>
    public PS2Keyboard(Intel8042Controller controller, State cpuState,
        IPauseHandler pauseHandler, ILoggerService loggerService,
        IGuiKeyboardEvents? guiKeyboardEvents = null) {
        _controller = controller;
        _cpuState = cpuState;
        _loggerService = loggerService;
        _pauseHandler = pauseHandler;

        _pauseHandler.Pausing += OnPausing;
        _pauseHandler.Resumed += OnResumed;

        KeyboardReset(isStartup: true);

        if (guiKeyboardEvents is not null) {
            guiKeyboardEvents.KeyDown += OnKeyEvent;
            guiKeyboardEvents.KeyUp += OnKeyEvent;
        }
    }

    private void OnPausing() {
        if (_pauseStartedAtTicks == 0) {
            _pauseStartedAtTicks = _rt.ElapsedTicks;
        }
    }
    private void OnResumed() {
        if (_pauseStartedAtTicks != 0) {
            var delta = _rt.ElapsedTicks - _pauseStartedAtTicks;
            _pausedAccumTicks += delta;
            _pauseStartedAtTicks = 0;
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
        if (_bufferNumUsed == 0 || !_controller.IsReadyForKbdFrame()) {
            return;
        }

        // Check for typematic repeat on buffer transfer (keyboard read)
        ProcessTypematic();

        // Check for LED timeout on buffer transfer (keyboard read)
        ProcessLedTimeout();

        _controller.AddKbdFrame(_buffer[_bufferStartIdx]);

        --_bufferNumUsed;
        _bufferStartIdx = (_bufferStartIdx + 1) % BufferSize;
    }

    private void BufferAdd(List<byte> scanCode) {
        // Ignore unsupported keys, drop everything if buffer overflowed
        if (scanCode == null || scanCode.Count == 0 || _bufferOverflowed) {
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
    // Key repetition
    // ***************************************************************************

    private void TypematicUpdate(KbdKey keyType, bool isPressed) {
        if (keyType is KbdKey.Pause or KbdKey.PrintScreen) {
            // Key is excluded from being repeated
        } else if (isPressed) {
            if (_repeat.Key == keyType) {
                _repeat.WaitTicks = NowTicks + _repeat.RateTicks;
            } else {
                _repeat.WaitTicks = NowTicks + _repeat.PauseTicks;
            }
            _repeat.Key = keyType;
        } else if (_repeat.Key == keyType) {
            // Currently repeated key being released
            _repeat.Key = KbdKey.None;
            _repeat.WaitTicks = 0;
        }
    }

    private void TypematicUpdateSet3(KbdKey keyType, List<byte> scanCode, bool isPressed) {
        // Ignore keys not supported in set 3
        if (scanCode == null || scanCode.Count == 0) {
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

    private void ProcessTypematic() {
        // No typematic key = nothing to do
        if (_repeat.Key == KbdKey.None) {
            return;
        }

        // Check if we should try to add key press
        if (_repeat.WaitTicks > 0 && NowTicks >= _repeat.WaitTicks) {
            // Check if buffers are free
            if (_bufferNumUsed > 0 || !_controller.IsReadyForKbdFrame()) {
                _repeat.WaitTicks = NowTicks + MsToTicks(1); // Try again in ~1ms
                return;
            }

            // Simulate key press
            const bool isPressed = true;
            AddKey(_repeat.Key, isPressed);
            _repeat.WaitTicks = NowTicks + _repeat.RateTicks; // Set for next repeat
        }
    }

    // ***************************************************************************
    // Keyboard microcontroller high-level emulation
    // ***************************************************************************

    private void MaybeNotifyLedState() {
        // TODO: add LED support to BIOS, currently it does not set them
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("KEYBOARD: LED state: {LedState:X2}", GetLedState());
        }
    }

    private void ProcessLedTimeout() {
        if (_ledsAllOn && _ledTimeoutTicks > 0 && NowTicks >= _ledTimeoutTicks) {
            LedsAllOnExpire();
        }
    }

    private void LedsAllOnExpire() {
        _ledsAllOn = false;
        _ledTimeoutTicks = 0;
        MaybeNotifyLedState();
    }

    private void ClearBuffer() {
        _bufferStartIdx = 0;
        _bufferNumUsed = 0;
        _bufferOverflowed = false;

        _repeat.Key = KbdKey.None;
        _repeat.WaitTicks = 0;
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
        // DOSBox tables in ms; convert to Stopwatch ticks
        long[] pauseTable = {
            MsToTicks(250), MsToTicks(500), MsToTicks(750), MsToTicks(1000)
        };
        long[] rateTable = {
             MsToTicks(33),  MsToTicks(37),  MsToTicks(42),  MsToTicks(46),  MsToTicks(50),  MsToTicks(54),  MsToTicks(58),  MsToTicks(63),
             MsToTicks(67),  MsToTicks(75),  MsToTicks(83),  MsToTicks(92),  MsToTicks(100), MsToTicks(109), MsToTicks(118), MsToTicks(125),
             MsToTicks(133), MsToTicks(149), MsToTicks(167), MsToTicks(182), MsToTicks(200), MsToTicks(217), MsToTicks(233), MsToTicks(250),
             MsToTicks(270), MsToTicks(303), MsToTicks(333), MsToTicks(370), MsToTicks(400), MsToTicks(435), MsToTicks(476), MsToTicks(500)
        };

        int pauseIdx = (value & 0b0110_0000) >> 5;
        int rateIdx = (value & 0b0001_1111);

        _repeat.PauseTicks = pauseTable[pauseIdx];
        _repeat.RateTicks = rateTable[rateIdx];
    }

    private void SetDefaults() {
        _repeat.Key = KbdKey.None;
        _repeat.PauseTicks = MsToTicks(500);
        _repeat.RateTicks = MsToTicks(33);
        _repeat.WaitTicks = 0;

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

        // Flash all the LEDs
        _ledTimeoutTicks = 0;
        _ledState = 0;
        _ledsAllOn = !isStartup;
        if (_ledsAllOn) {
            _ledTimeoutTicks = NowTicks + MsToTicks(666);
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
                _controller.AddKbdByte(0xfa); // acknowledge
                _currentCommand = command;
                break;
            case KeyboardCommand.CodeSet:          // 0xf0
            case KeyboardCommand.Set3KeyTypematic: // 0xfb
            case KeyboardCommand.Set3KeyMakeBreak: // 0xfc
            case KeyboardCommand.Set3KeyMakeOnly:  // 0xfd
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                _currentCommand = command;
                break;
            //
            // No-parameter commands
            //
            case KeyboardCommand.Echo: // 0xee
                // Diagnostic echo, responds without acknowledge
                _controller.AddKbdByte(0xee);
                break;
            case KeyboardCommand.Identify: // 0xf2
                // Returns keyboard ID
                // - 0xab, 0x83: typical for multifunction PS/2 keyboards
                // - 0xab, 0x84: many short, space saver keyboards
                // - 0xab, 0x86: many 122-key keyboards
                _controller.AddKbdByte(0xfa); // acknowledge
                _controller.AddKbdByte(0xab);
                _controller.AddKbdByte(0x83);
                break;
            case KeyboardCommand.ClearEnable: // 0xf4
                // Clear internal buffer, enable scanning
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                _isScanning = true;
                break;
            case KeyboardCommand.DefaultDisable: // 0xf5
                // Restore defaults, disable scanning
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                SetDefaults();
                _isScanning = false;
                break;
            case KeyboardCommand.ResetEnable: // 0xf6
                // Restore defaults, enable scanning
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                SetDefaults();
                _isScanning = true;
                break;
            case KeyboardCommand.Set3AllTypematic: // 0xf7
                // Set scanning type for all the keys,
                // relevant for scancode set 3 only
                _controller.AddKbdByte(0xfa); // acknowledge
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
                _controller.AddKbdByte(0xfa); // acknowledge
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
                _controller.AddKbdByte(0xfa); // acknowledge
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
                _controller.AddKbdByte(0xfa); // acknowledge
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
                _controller.AddKbdByte(0xfa); // acknowledge
                break;
            case KeyboardCommand.Reset: // 0xff
                // Full keyboard reset and self test
                // 0xaa: passed; 0xfc/0xfd: failed
                _controller.AddKbdByte(0xfa); // acknowledge
                KeyboardReset();
                _controller.AddKbdByte(0xaa);
                break;
            //
            // Unknown commands
            //
            default:
                WarnUnknownCommand(command);
                _controller.AddKbdByte(0xfe); // resend
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
                _controller.AddKbdByte(0xfa); // acknowledge
                _ledState = param;
                MaybeNotifyLedState();
                break;
            case KeyboardCommand.CodeSet: // 0xf0
                // Query or change the scancode set
                if (param != 0) {
                    // Change scancode set
                    if (SetCodeSet(param)) {
                        _controller.AddKbdByte(0xfa); // acknowledge
                    } else {
                        _currentCommand = command;
                        _controller.AddKbdByte(0xfe); // resend
                    }
                } else {
                    // Query current scancode set
                    _controller.AddKbdByte(0xfa); // acknowledge
                    _controller.AddKbdByte(_codeSet);
                }
                break;
            case KeyboardCommand.SetTypeRate: // 0xf3
                // Sets typematic rate/delay
                _controller.AddKbdByte(0xfa); // acknowledge
                SetTypeRate(param);
                break;
            case KeyboardCommand.Set3KeyTypematic: // 0xfb
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                GetSet3CodeInfo(param).IsEnabledTypematic = true;
                GetSet3CodeInfo(param).IsEnabledMake = false;
                GetSet3CodeInfo(param).IsEnabledBreak = false;
                break;
            case KeyboardCommand.Set3KeyMakeBreak: // 0xfc
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte(0xfa); // acknowledge
                ClearBuffer();
                GetSet3CodeInfo(param).IsEnabledTypematic = false;
                GetSet3CodeInfo(param).IsEnabledMake = true;
                GetSet3CodeInfo(param).IsEnabledBreak = true;
                break;
            case KeyboardCommand.Set3KeyMakeOnly: // 0xfd
                // Set scanning type for the given key,
                // relevant for scancode set 3 only
                _controller.AddKbdByte(0xfa); // acknowledge
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