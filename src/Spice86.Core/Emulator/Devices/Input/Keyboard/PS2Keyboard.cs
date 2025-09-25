namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

/// <summary>
/// PS/2 keyboard emulation. C# port of DOSBox's keyboard.cpp
/// </summary>
public class PS2Keyboard {
    private readonly Intel8042Controller _controller;
    private readonly ILoggerService _loggerService;
    private readonly KeyboardScancodeConverter _scancodeConverter = new();
    private readonly State _cpuState;

    // Internal keyboard scancode buffer - mirrors DOSBox implementation
    private const int BufferSize = 8; // in scancodes
    private readonly List<byte>[] _buffer = new List<byte>[BufferSize];
    private bool _bufferOverflowed = false;
    private int _bufferStartIdx = 0;
    private int _bufferNumUsed = 0;

    // Key repetition mechanism data - mirrors DOSBox struct
    private struct RepeatData {
        public KbdKey Key;      // key which went typematic
        public long Wait;       // countdown timer in CPU cycles
        public long Pause;      // initial delay in CPU cycles
        public long Rate;       // repeat rate in CPU cycles
    }
    private RepeatData _repeat = new() { Key = KbdKey.None, Wait = 0, Pause = 500000, Rate = 33000 }; // Default values in cycles

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
    // LED timeout tracking in CPU cycles
    private long _ledTimeoutCycles = 0;
    // If false, keyboard does not push keycodes to the controller
    private bool _isScanning = true;

    private byte _codeSet = 1;

    // Command currently being executed, waiting for parameter
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    // CPU cycles conversion constants (approximate)
    private const long CyclesPerMs = 10;

    /// <summary>
    /// Initializes a new instance of the <see cref="PS2Keyboard"/> class.
    /// </summary>
    /// <param name="controller">The keyboard controller.</param>
    /// <param name="cpuState">The CPU state for cycle-based timing.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    /// <param name="guiKeyboardEvents">Optional GUI keyboard events interface.</param>
    public PS2Keyboard(Intel8042Controller controller, State cpuState,
        ILoggerService loggerService, IGuiKeyboardEvents? guiKeyboardEvents = null) {
        _controller = controller;
        _cpuState = cpuState;
        _loggerService = loggerService;

        KeyboardReset(isStartup: true);

        if (guiKeyboardEvents is not null) {
            guiKeyboardEvents.KeyDown += OnKeyEvent;
            guiKeyboardEvents.KeyUp += OnKeyEvent;
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
                _repeat.Wait = _cpuState.Cycles + _repeat.Rate;
            } else {
                _repeat.Wait = _cpuState.Cycles + _repeat.Pause;
            }
            _repeat.Key = keyType;
        } else if (_repeat.Key == keyType) {
            // Currently repeated key being released
            _repeat.Key = KbdKey.None;
            _repeat.Wait = 0;
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
        if (_repeat.Wait > 0 && _cpuState.Cycles >= _repeat.Wait) {
            // Check if buffers are free
            if (_bufferNumUsed > 0 || !_controller.IsReadyForKbdFrame()) {
                _repeat.Wait = _cpuState.Cycles + CyclesPerMs; // Try again in ~1ms worth of cycles
                return;
            }

            // Simulate key press
            const bool isPressed = true;
            AddKey(_repeat.Key, isPressed);
            _repeat.Wait = _cpuState.Cycles + _repeat.Rate; // Set for next repeat
        }
    }

    // ***************************************************************************
    // Keyboard microcontroller high-level emulation
    // ***************************************************************************

    private void MaybeNotifyLedState() {
        // TODO: add LED support to BIOS, currently it does not set them
        // consider displaying LEDs on screen

        byte currentState = GetLedState();
        // Here you could add UI notification about LED state changes
        // or log it for debug purposes
        if (_loggerService.IsEnabled(LogEventLevel.Debug)) {
            _loggerService.Debug("KEYBOARD: LED state: {LedState:X2}", currentState);
        }
    }

    private void ProcessLedTimeout() {
        if (_ledsAllOn && _ledTimeoutCycles > 0 && _cpuState.Cycles >= _ledTimeoutCycles) {
            LedsAllOnExpire();
        }
    }

    private void LedsAllOnExpire() {
        _ledsAllOn = false;
        _ledTimeoutCycles = 0;
        MaybeNotifyLedState();
    }

    private void ClearBuffer() {
        _bufferStartIdx = 0;
        _bufferNumUsed = 0;
        _bufferOverflowed = false;

        _repeat.Key = KbdKey.None;
        _repeat.Wait = 0;
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
        long[] pauseTable = { 250 * CyclesPerMs, 500 * CyclesPerMs, 750 * CyclesPerMs, 1000 * CyclesPerMs };
        long[] rateTable = {
             33 * CyclesPerMs,  37 * CyclesPerMs,  42 * CyclesPerMs,  46 * CyclesPerMs,  50 * CyclesPerMs,  54 * CyclesPerMs,  58 * CyclesPerMs,  63 * CyclesPerMs,
             67 * CyclesPerMs,  75 * CyclesPerMs,  83 * CyclesPerMs,  92 * CyclesPerMs, 100 * CyclesPerMs, 109 * CyclesPerMs, 118 * CyclesPerMs, 125 * CyclesPerMs,
            133 * CyclesPerMs, 149 * CyclesPerMs, 167 * CyclesPerMs, 182 * CyclesPerMs, 200 * CyclesPerMs, 217 * CyclesPerMs, 233 * CyclesPerMs, 250 * CyclesPerMs,
            270 * CyclesPerMs, 303 * CyclesPerMs, 333 * CyclesPerMs, 370 * CyclesPerMs, 400 * CyclesPerMs, 435 * CyclesPerMs, 476 * CyclesPerMs, 500 * CyclesPerMs
        };

        int pauseIdx = (value & 0b0110_0000) >> 5;
        int rateIdx = (value & 0b0001_1111);

        _repeat.Pause = pauseTable[pauseIdx];
        _repeat.Rate = rateTable[rateIdx];
    }

    private void SetDefaults() {
        _repeat.Key = KbdKey.None;
        _repeat.Pause = 500 * CyclesPerMs;
        _repeat.Rate = 33 * CyclesPerMs;
        _repeat.Wait = 0;

        foreach (Set3CodeInfoEntry entry in _set3CodeInfo.Values) {
            entry.IsEnabledMake = true;
            entry.IsEnabledBreak = true;
            entry.IsEnabledTypematic = true;
        }

        SetCodeSet(1); // Default to codeset 1
    }

    private void KeyboardReset(bool isStartup = false) {
        SetDefaults();
        ClearBuffer();

        _isScanning = true;

        // Flash all the LEDs
        _ledTimeoutCycles = 0;
        _ledState = 0;
        _ledsAllOn = !isStartup;
        if (_ledsAllOn) {
            _ledTimeoutCycles = _cpuState.Cycles + (666 * CyclesPerMs);
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
        // NOTE: Skipping secure mode check as requested
        // if (_shouldWaitForSecureMode && !control->SecureMode()) {
        //     WarnWaitingForSecureMode();
        //     return;
        // }

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