namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// PS/2 keyboard emulation. C# port of DOSBox's keyboard.cpp
/// </summary>
public sealed class PS2Keyboard {
    private readonly Intel8042Controller _controller;
    private readonly ILoggerService _loggerService;
    private readonly KeyboardScancodeConverter _scancodeConverter = new();
    private readonly EmulatorEventClock _eventClock;

    // Typematic/repeat mechanism (mirrors DOSBox's implementation)
    private KeyboardEventArgs _repeatKey = KeyboardEventArgs.None;
    private uint _repeatWait = 0;
    private uint _repeatPause = 500; // Default typematic pause (ms)
    private uint _repeatRate = 33;   // Default typematic rate (ms)

    // Default codeset 1 as in DOSBox
    private byte _codeSet = 1;

    // Command state waiting for parameter within keyboard domain
    private KeyboardCommand _currentKbdCommand = KeyboardCommand.None;

    // LED state management
    private byte _ledState = 0;
    private bool _ledsAllOn = false;

    // If false, keyboard does not push keycodes to the controller
    private bool _isScanning = true;

    // Set3-specific code info
    private class Set3CodeInfoEntry {
        public bool IsEnabledTypematic = true;
        public bool IsEnabledMake = true;
        public bool IsEnabledBreak = true;
    }

    private readonly Dictionary<byte, Set3CodeInfoEntry> _set3CodeInfo = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PS2Keyboard"/> class.
    /// </summary>
    /// <param name="controller">The keyboard controller.</param>
    /// <param name="eventClock">The emulator event clock.</param>
    /// <param name="loggerService">The logger service implementation.</param>
    public PS2Keyboard(Intel8042Controller controller, EmulatorEventClock eventClock,
        ILoggerService loggerService, IGuiKeyboardEvents? guiKeyboardEvents = null) {
        _controller = controller;
        _eventClock = eventClock;
        _loggerService = loggerService;

        // Initialize keyboard state with startup flag
        KeyboardReset(isStartup: true);

        if(guiKeyboardEvents is not null) {
            guiKeyboardEvents.KeyDown += OnKeyEvent;
            guiKeyboardEvents.KeyUp += OnKeyEvent;
        }
    }

    /// <summary>
    /// Handles keyboard key events from the UI.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The keyboard event arguments.</param>
    public void OnKeyEvent(object? sender, KeyboardEventArgs e) {
        if (!_isScanning) {
            return;
        }

        // Get the appropriate scancode for the key based on code set
        List<byte> scanCode = GetScanCode(e.Key, e.IsPressed);

        // Update typematic state based on code set
        switch (_codeSet) {
            case 1:
                TypematicUpdate(e);
                break;
            case 2:
                TypematicUpdate(e);
                break;
            case 3:
                TypematicUpdateSet3(e, scanCode);
                break;
        }

        // Add the scancode to the buffer
        AddKeyFrame(scanCode);
    }

    private List<byte> GetScanCode(PhysicalKey key, bool isPressed) {
        // Convert PhysicalKey to KbdKey (assuming you have this mapping)
        KbdKey kbdKey = ConvertToKbdKey(key);

        switch (_codeSet) {
            case 2:
                return _scancodeConverter.GetScanCode2(kbdKey, isPressed);
            case 3:
                return _scancodeConverter.GetScanCode3(kbdKey, isPressed);
            case 1:
            default:
                return _scancodeConverter.GetScanCode1(kbdKey, isPressed);
        }
    }

    private KbdKey ConvertToKbdKey(PhysicalKey key) {
        // This method would map PhysicalKey to your KbdKey enum
        // Implementation depends on your key enums
        return KbdKey.None; // Placeholder
    }

    private void AddKeyFrame(List<byte> scancodes) {
        if (scancodes == null || scancodes.Count == 0) {
            return;
        }

        MaybeTransferFrameToController();
        _controller.AddKbdFrame(scancodes);
    }

    // Mirror keyboard.cpp maybe_transfer_buffer that transfers a frame to I8042_AddKbdFrame
    private void MaybeTransferFrameToController() {
        // Controller handles the actual buffer management
    }

    /// <summary>
    /// Called when the controller is ready to accept keyboard frames.
    /// </summary>
    internal void NotifyReadyForFrame() {
        // This is called by the controller when it's ready to accept frames
        // Nothing to do here as the controller handles the buffering
    }

    // Add a single byte as keyboard would do (I8042_AddKbdByte)
    internal void AddKbdByte(byte b) {
        _controller.AddKbdByte(b);
    }

    /// <summary>
    /// Handles writes to the keyboard port from the controller.
    /// </summary>
    /// <param name="value">The value written to the port.</param>
    public void PortWrite(byte value) {
        // Similar to KEYBOARD_PortWrite in keyboard.cpp
        // Check if it's a command
        bool isCommand = (value & 0x80) != 0 &&
                        _currentKbdCommand != KeyboardCommand.Set3KeyTypematic &&
                        _currentKbdCommand != KeyboardCommand.Set3KeyMakeBreak &&
                        _currentKbdCommand != KeyboardCommand.Set3KeyMakeOnly;

        if (isCommand) {
            // Terminate previous command
            _currentKbdCommand = KeyboardCommand.None;
        }

        KeyboardCommand command = _currentKbdCommand;
        if (command != KeyboardCommand.None) {
            // Continue execution of previous command
            _currentKbdCommand = KeyboardCommand.None;
            ExecuteKeyboardCommand(command, value);
        } else if (isCommand) {
            ExecuteKeyboardCommand((KeyboardCommand)value);
        }
    }

    private void ExecuteKeyboardCommand(KeyboardCommand command) {
        switch (command) {
            // Commands requiring a parameter
            case KeyboardCommand.SetLeds:
            case KeyboardCommand.SetTypeRate:
            case KeyboardCommand.CodeSet:
            case KeyboardCommand.Set3KeyTypematic:
            case KeyboardCommand.Set3KeyMakeBreak:
            case KeyboardCommand.Set3KeyMakeOnly:
                AddKbdByte(0xFA); // acknowledge
                _currentKbdCommand = command;
                break;

            // No-parameter commands
            case KeyboardCommand.Echo: // 0xEE
                // Diagnostic echo, responds without acknowledge
                AddKbdByte(0xEE);
                break;
            case KeyboardCommand.Identify: // 0xF2
                // Returns keyboard ID
                AddKbdByte(0xFA); // acknowledge
                AddKbdByte(0xAB);
                AddKbdByte(0x83);
                break;
            case KeyboardCommand.ClearEnable: // 0xF4
                // Clear internal buffer, enable scanning
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                _isScanning = true;
                break;
            case KeyboardCommand.DefaultDisable: // 0xF5
                // Restore defaults, disable scanning
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetDefaults();
                _isScanning = false;
                break;
            case KeyboardCommand.ResetEnable: // 0xF6
                // Restore defaults, enable scanning
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetDefaults();
                _isScanning = true;
                break;
            case KeyboardCommand.Set3AllTypematic: // 0xF7
                // Set scanning type for all keys (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetAllKeysSet3Properties(typematic: true, make: false, breakState: false);
                break;
            case KeyboardCommand.Set3AllMakeBreak: // 0xF8
                // Set scanning type for all keys (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetAllKeysSet3Properties(typematic: false, make: true, breakState: true);
                break;
            case KeyboardCommand.Set3AllMakeOnly: // 0xF9
                // Set scanning type for all keys (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetAllKeysSet3Properties(typematic: false, make: true, breakState: false);
                break;
            case KeyboardCommand.Set3AllTypeMakeBreak: // 0xFA
                // Set scanning type for all keys (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetAllKeysSet3Properties(typematic: true, make: true, breakState: true);
                break;
            case KeyboardCommand.Resend: // 0xFE
                // Resend byte
                WarnResend();
                AddKbdByte(0xFA); // acknowledge for compatibility
                break;
            case KeyboardCommand.Reset: // 0xFF
                // Full keyboard reset and self test
                AddKbdByte(0xFA); // acknowledge
                KeyboardReset();
                AddKbdByte(0xAA); // self test passed
                break;
            default:
                WarnUnknownKeyboardCommand(command);
                AddKbdByte(0xFE); // resend request
                break;
        }
    }

    private void ExecuteKeyboardCommand(KeyboardCommand command, byte param) {
        switch (command) {
            case KeyboardCommand.SetLeds: // 0xED
                // Set keyboard LEDs according to bitfield
                AddKbdByte(0xFA); // acknowledge
                _ledState = param;
                MaybeNotifyLedState();
                break;
            case KeyboardCommand.CodeSet: // 0xF0
                // Query or change the scancode set
                if (param != 0) {
                    // Change scancode set
                    if (SetCodeSet(param)) {
                        AddKbdByte(0xFA); // acknowledge
                    } else {
                        _currentKbdCommand = command;
                        AddKbdByte(0xFE); // resend
                    }
                } else {
                    // Query current scancode set
                    AddKbdByte(0xFA); // acknowledge
                    AddKbdByte(_codeSet);
                }
                break;
            case KeyboardCommand.SetTypeRate: // 0xF3
                // Sets typematic rate/delay
                AddKbdByte(0xFA); // acknowledge
                SetTypeRate(param);
                break;
            case KeyboardCommand.Set3KeyTypematic: // 0xFB
                // Set scanning type for the given key (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetKeySet3Properties(param, typematic: true, make: false, breakState: false);
                break;
            case KeyboardCommand.Set3KeyMakeBreak: // 0xFC
                // Set scanning type for the given key (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetKeySet3Properties(param, typematic: false, make: true, breakState: true);
                break;
            case KeyboardCommand.Set3KeyMakeOnly: // 0xFD
                // Set scanning type for the given key (scancode set 3)
                AddKbdByte(0xFA); // acknowledge
                ClearInternalBuffer();
                SetKeySet3Properties(param, typematic: false, make: true, breakState: false);
                break;
            default:
                // If we are here, then either this function was wrongly called or it is incomplete
                break;
        }
    }

    private void TypematicUpdate(KeyboardEventArgs e) {
        if (e.Key == PhysicalKey.Pause || e.Key == PhysicalKey.PrintScreen) {
            // Key is excluded from being repeated
            return;
        } else if (e.IsPressed) {
            if (_repeatKey.Key == e.Key) {
                _repeatWait = _repeatRate;
            } else {
                _repeatWait = _repeatPause;
            }
            _repeatKey = e;
        } else if (_repeatKey.Key == e.Key) {
            // Currently repeated key being released
            _repeatKey = KeyboardEventArgs.None;
            _repeatWait = 0;
            StopTypematicTimer();
        }

        // If we're setting up repeat, ensure the timer is running
        if (_repeatKey.Key != PhysicalKey.None) {
            StartTypematicTimer();
        }
    }

    private void TypematicUpdateSet3(KeyboardEventArgs e, List<byte> scanCode) {
        // Ignore keys not supported in set 3
        if (scanCode == null || scanCode.Count == 0) {
            return;
        }

        // Get the scan code for typematic check
        byte code = e.IsPressed
            ? scanCode[0]  // Make code is the first byte
            : scanCode[1]; // Break code is second byte after 0xF0

        // Check if this key has typematic behavior enabled
        if (!GetKeySet3Info(code).IsEnabledTypematic) {
            return;
        }

        // For all other keys, follow usual behavior
        TypematicUpdate(e);
    }

    private void StartTypematicTimer() {
        StopTypematicTimer();
        _eventClock.AddEvent(TypematicTick, 10, "KeyboardTypematic"); // Check every 10ms
    }

    private void StopTypematicTimer() {
        _eventClock.RemoveEvent("KeyboardTypematic");
    }

    private void TypematicTick() {
        // Update countdown, check if we should try to add key press
        if (_repeatWait > 0) {
            _repeatWait -= 10; // Decrease by timer interval
            if (_repeatWait > 0) {
                // Not time yet, schedule next check
                StartTypematicTimer();
                return;
            }
        }

        // No typematic key = nothing to do
        if (_repeatKey.Key == PhysicalKey.None) {
            return;
        }

        // Simulate key press
        OnKeyEvent(this, new KeyboardEventArgs(_repeatKey.Key, true));
        _repeatWait = _repeatRate; // Set for next repeat
        StartTypematicTimer();
    }

    private void SetTypeRate(byte b) {
        // Calculate pause and rate from the parameter
        // Bit 5-6: pause duration
        int pauseIdx = (b & 0b0110_0000) >> 5;
        // Bit 0-4: rate
        int rateIdx = (b & 0b0001_1111);

        // These tables match DOSBox's implementation
        uint[] pauseTable = { 250, 500, 750, 1000 };
        uint[] rateTable = {
            33,  37,  42,  46,  50,  54,  58,  63,
            67,  75,  83,  92, 100, 109, 118, 125,
            133, 149, 167, 182, 200, 217, 233, 250,
            270, 303, 333, 370, 400, 435, 476, 500
        };

        _repeatPause = pauseTable[pauseIdx];
        _repeatRate = rateTable[rateIdx];
    }

    private void SetDefaults() {
        _repeatKey = KeyboardEventArgs.None;
        _repeatPause = 500;
        _repeatRate = 33;
        _repeatWait = 0;

        // Reset all set3 code info entries to default values
        foreach (byte key in _set3CodeInfo.Keys) {
            SetKeySet3Properties(key, true, true, true);
        }

        SetCodeSet(1); // Default to codeset 1
    }

    private bool SetCodeSet(byte requestedSet) {
        if (requestedSet < 1 || requestedSet > 3) {
            WarnUnknownScancodeSet();
            return false;
        }

        // Change code set if it's different
        byte oldSet = _codeSet;
        _codeSet = requestedSet;

        if (_codeSet != oldSet) {
            if (_loggerService.IsEnabled(LogEventLevel.Information)) {
                _loggerService.Information("KEYBOARD: Using scancode set #{CodeSet}", _codeSet);
            }
        }

        ClearInternalBuffer();
        return true;
    }

    private void KeyboardReset(bool isStartup = false) {
        SetDefaults();
        ClearInternalBuffer();
        _isScanning = true;

        // Flash all LEDs
        _eventClock.RemoveEvent("KeyboardLEDsOff");
        _ledState = 0;
        _ledsAllOn = !isStartup;

        if (_ledsAllOn) {
            // Set LEDs expiration timer (666ms as in DOSBox)
            _eventClock.AddEvent(LedsAllOnExpire, 666, "KeyboardLEDsOff");
        }

        MaybeNotifyLedState();
    }

    private void LedsAllOnExpire() {
        _ledsAllOn = false;
        MaybeNotifyLedState();
    }

    private void MaybeNotifyLedState() {
        byte currentState = GetLedState();
        // Here you could add UI notification about LED state changes
        // or log it for debug purposes
    }

    /// <summary>
    /// Gets the current LED state.
    /// </summary>
    /// <returns>The LED state byte.</returns>
    public byte GetLedState() {
        // We support only 3 LEDs (bits 0-2)
        return (byte)((_ledsAllOn ? 0xFF : _ledState) & 0b0000_0111);
    }

    private void ClearInternalBuffer() {
        // Clear any internal buffer state
        _repeatKey = KeyboardEventArgs.None;
        _repeatWait = 0;
        StopTypematicTimer();
    }

    private void SetAllKeysSet3Properties(bool typematic, bool make, bool breakState) {
        // Initialize dictionary if needed
        for (byte i = 0; i < 255; i++) {
            SetKeySet3Properties(i, typematic, make, breakState);
        }
    }

    private void SetKeySet3Properties(byte scancode, bool typematic, bool make, bool breakState) {
        if (!_set3CodeInfo.TryGetValue(scancode, out Set3CodeInfoEntry? entry)) {
            entry = new Set3CodeInfoEntry();
            _set3CodeInfo[scancode] = entry;
        }

        entry.IsEnabledTypematic = typematic;
        entry.IsEnabledMake = make;
        entry.IsEnabledBreak = breakState;
    }

    private Set3CodeInfoEntry GetKeySet3Info(byte scancode) {
        if (!_set3CodeInfo.TryGetValue(scancode, out Set3CodeInfoEntry? entry)) {
            // Create default entry
            entry = new Set3CodeInfoEntry();
            _set3CodeInfo[scancode] = entry;
        }
        return entry;
    }

    private void WarnResend() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Resend command not implemented");
        }
    }

    private void WarnUnknownKeyboardCommand(KeyboardCommand cmd) {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Unknown command 0x{Command:X2}", (byte)cmd);
        }
    }

    private void WarnUnknownScancodeSet() {
        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
            _loggerService.Warning("KEYBOARD: Guest requested unknown scancode set");
        }
    }
}