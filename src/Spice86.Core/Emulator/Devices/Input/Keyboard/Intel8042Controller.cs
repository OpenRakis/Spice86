namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// C# port of intel8042.cpp - The PS/2 keyboard and mouse controller.
/// </summary>
public class Intel8042Controller : DefaultIOPortHandler {
    private const byte IrqNumKbdIbmPc = 1;
    private const byte IrqNumMouse = 12;

    private const byte FirmwareRevision = 0x00;
    private const string FirmwareCopyright = nameof(Spice86);

    private const int BufferSize = 64; // in bytes
    // delay appropriate for 20-30 kHz serial clock and 11 bits/byte
    private const double PortDelayMs = 0.300; // 0.3ms

    // Controller internal buffer
    private struct BufferEntry {
        public byte Data;
        public bool IsFromAux;
        public bool IsFromKbd;
        public bool SkipDelay;
    }

    private readonly BufferEntry[] _buffer = new BufferEntry[BufferSize];
    private int _bufferStartIdx = 0;
    private int _bufferNumUsed = 0;

    private int _waitingBytesFromAux = 0;
    private int _waitingBytesFromKbd = 0;

    // CPU cycle-based delay tracking
    private long _delayExpiryCycles = 0;
    private bool _delayActive = false;

    // Executing command, do not notify devices about readiness for accepting frame
    private bool _shouldSkipDeviceNotify = false;

    // Command currently being executed, waiting for parameter
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    // Byte 0x00 of the controller memory - configuration byte
    private byte _configByte = 0b0000_0111;

    // Byte returned from port 0x60
    private byte _dataByte = 0;

    // Byte returned from port 0x64
    private byte _statusByte = 0b0001_1100;

    // If enabled, all keyboard events are dropped until dump is done
    private bool _isDiagnosticDump = false;

    private readonly bool[] _unknownCommandWarned = new bool[256];
    private bool _bufferFullWarned = false;
    private long _lastTimeStamp = 0;
    private bool _controllerModeWarned = false;
    private bool _internalRamWarned = false;
    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;
    private readonly State _cpuState;

    public PS2Keyboard KeyboardDevice { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Intel8042Controller"/> class.
    /// </summary>
    public Intel8042Controller(State state, IOPortDispatcher ioPortDispatcher,
        A20Gate a20Gate, DualPic dualPic,
        ILoggerService loggerService, bool failOnUnhandledPort,
        IGuiKeyboardEvents? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        _cpuState = state;

        // Create the keyboard implementation
        KeyboardDevice = new PS2Keyboard(this, state, loggerService, gui);

        InitPortHandlers(ioPortDispatcher);

        // Initialize hardware
        FlushBuffer();
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Command, this);
    }

    // ***************************************************************************
    // Helper routines to log various warnings
    // ***************************************************************************

    private void WarnBufferFull() {
        const uint thresoldCycles = 150;

        // Static-like behavior using class fields
        if (!_bufferFullWarned || (_cpuState.Cycles - _lastTimeStamp > thresoldCycles)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Internal buffer overflow");
            }
            _lastTimeStamp = _cpuState.Cycles;
            _bufferFullWarned = true;
        }
    }

    private void WarnControllerMode() {
        if (!_controllerModeWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Switching controller to AT mode not emulated");
            }
            _controllerModeWarned = true;
        }
    }

    private void WarnInternalRamAccess() {
        if (!_internalRamWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Accessing internal RAM (other than byte 0x00) gives vendor-specific results");
            }
            _internalRamWarned = true;
        }
    }

    private void WarnLinePulse() {
        if (!_linePulseWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Pulsing line other than RESET not emulated");
            }
            _linePulseWarned = true;
        }
    }
    private bool _linePulseWarned = false;

    private void WarnReadTestInputs() {
        if (!_readTestInputsWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Reading test inputs not implemented");
            }
            _readTestInputsWarned = true;
        }
    }
    private bool _readTestInputsWarned = false;

    private void WarnVendorLines() {
        if (!_vendorLinesWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: No vendor-specific commands to manipulate controller lines are emulated");
            }
            _vendorLinesWarned = true;
        }
    }
    private bool _vendorLinesWarned = false;

    private void WarnUnknownCommand(KeyboardCommand command) {
        byte code = (byte)command;
        if (!_unknownCommandWarned[code]) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Unknown command 0x{Command:X2}", code);
            }
            _unknownCommandWarned[code] = true;
        }
    }

    // ***************************************************************************
    // XT translation for keyboard input
    // ***************************************************************************

    private static byte GetTranslated(byte value) {
        // A drain bamaged keyboard input translation

        // Intended to make scancode set 2 compatible with software knowing
        // only scancode set 1. Translates every byte coming from the keyboard,
        // scancodes and command responses alike!

        // Values from 86Box source code, can also be found in many other places

        byte[] translationTable = {
            0xff, 0x43, 0x41, 0x3f, 0x3d, 0x3b, 0x3c, 0x58,
            0x64, 0x44, 0x42, 0x40, 0x3e, 0x0f, 0x29, 0x59,
            0x65, 0x38, 0x2a, 0x70, 0x1d, 0x10, 0x02, 0x5a,
            0x66, 0x71, 0x2c, 0x1f, 0x1e, 0x11, 0x03, 0x5b,
            0x67, 0x2e, 0x2d, 0x20, 0x12, 0x05, 0x04, 0x5c,
            0x68, 0x39, 0x2f, 0x21, 0x14, 0x13, 0x06, 0x5d,
            0x69, 0x31, 0x30, 0x23, 0x22, 0x15, 0x07, 0x5e,
            0x6a, 0x72, 0x32, 0x24, 0x16, 0x08, 0x09, 0x5f,
            0x6b, 0x33, 0x25, 0x17, 0x18, 0x0b, 0x0a, 0x60,
            0x6c, 0x34, 0x35, 0x26, 0x27, 0x19, 0x0c, 0x61,
            0x6d, 0x73, 0x28, 0x74, 0x1a, 0x0d, 0x62, 0x6e,
            0x3a, 0x36, 0x1c, 0x1b, 0x75, 0x2b, 0x63, 0x76,
            0x55, 0x56, 0x77, 0x78, 0x79, 0x7a, 0x0e, 0x7b,
            0x7c, 0x4f, 0x7d, 0x4b, 0x47, 0x7e, 0x7f, 0x6f,
            0x52, 0x53, 0x50, 0x4c, 0x4d, 0x48, 0x01, 0x45,
            0x57, 0x4e, 0x51, 0x4a, 0x37, 0x49, 0x46, 0x54,
            0x80, 0x81, 0x82, 0x41, 0x54, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f,
            0x90, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97,
            0x98, 0x99, 0x9a, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f,
            0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
            0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf,
            0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7,
            0xb8, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe, 0xbf,
            0xc0, 0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7,
            0xc8, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 0xce, 0xcf,
            0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7,
            0xd8, 0xd9, 0xda, 0xdb, 0xdc, 0xdd, 0xde, 0xdf,
            0xe0, 0xe1, 0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7,
            0xe8, 0xe9, 0xea, 0xeb, 0xec, 0xed, 0xee, 0xef,
            0xf0, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7,
            0xf8, 0xf9, 0xfa, 0xfb, 0xfc, 0xfd, 0xfe, 0xff,
        };

        return translationTable[value];
    }

    private void ActivateIrqsIfNeeded() {
        bool isDataFromAux = (_statusByte & (1 << 5)) != 0;
        bool isDataFromKbd = !isDataFromAux && (_statusByte & (1 << 0)) != 0;

        if (isDataFromAux && (_configByte & (1 << 1)) != 0) {
            _dualPic.ProcessInterruptRequest(IrqNumMouse);
        }
        if (isDataFromKbd && (_configByte & (1 << 0)) != 0) {
            _dualPic.ProcessInterruptRequest(IrqNumKbdIbmPc);
        }
    }

    private void FlushBuffer() {
        _statusByte &= 0xFE; // is_data_new = false (0xFE = ~(1 << 0))
        _statusByte &= 0xDF; // is_data_from_aux = false (0xDF = ~(1 << 5))
        // is_data_from_kbd tracked separately by buffer contents

        _bufferStartIdx = 0;
        _bufferNumUsed = 0;

        bool shouldNotifyAux = !_shouldSkipDeviceNotify && !IsReadyForAuxFrame();
        bool shouldNotifyKbd = !_shouldSkipDeviceNotify && !IsReadyForKbdFrame();

        _waitingBytesFromAux = 0;
        _waitingBytesFromKbd = 0;

        if (shouldNotifyAux && IsReadyForAuxFrame()) {
            // MOUSEPS2_NotifyReadyForFrame(); // TODO: Mouse frame support...
        }

        if (shouldNotifyKbd && IsReadyForKbdFrame()) {
            KeyboardDevice.NotifyReadyForFrame();
        }
    }

    private void EnforceBufferSpace(int numBytes = 1) {
        if (numBytes > BufferSize) {
            throw new ArgumentOutOfRangeException(nameof(numBytes));
        }

        if (BufferSize < _bufferNumUsed + numBytes) {
            WarnBufferFull();
            FlushBuffer();
        }
    }

    private void RestartDelayTimer(double timeMs = PortDelayMs) {
        _delayExpiryCycles = (long)(_cpuState.Cycles + timeMs);
        _delayActive = true;
    }

    private bool IsDelayExpired() {
        if (!_delayActive) {
            return true;
        }

        if (_cpuState.Cycles >= _delayExpiryCycles) {
            _delayActive = false;
            return true;
        }

        return false;
    }

    private void MaybeTransferBuffer() {
        if ((_statusByte & (1 << 0)) != 0 || _bufferNumUsed == 0) {
            // There is already some data waiting to be picked up,
            // or there is nothing waiting in the buffer
            return;
        }

        // If not set to skip the delay, do not send byte until timer expires
        int idx = _bufferStartIdx;
        if (!IsDelayExpired() && !_buffer[idx].SkipDelay) {
            return;
        }

        // Mark byte as consumed
        _bufferStartIdx = (_bufferStartIdx + 1) % BufferSize;
        --_bufferNumUsed;

        // Transfer one byte of data from buffer to output port
        _dataByte = _buffer[idx].Data;
        if (_buffer[idx].IsFromAux) {
            _statusByte |= (1 << 5); // is_data_from_aux = true
        } else {
            _statusByte &= 0xDF; // is_data_from_aux = false (0xDF = ~(1 << 5))
        }
        _statusByte |= (1 << 0); // is_data_new = true
        RestartDelayTimer();
        ActivateIrqsIfNeeded();
    }

    private void BufferAdd(byte value, bool isFromAux = false, bool isFromKbd = false, bool skipDelay = false) {
        if ((isFromAux && (_configByte & (1 << 5)) != 0) ||
            (isFromKbd && (_configByte & (1 << 4)) != 0)) {
            // Byte came from a device which is currently disabled
            return;
        }

        if (_bufferNumUsed >= BufferSize) {
            WarnBufferFull();
            FlushBuffer();
            return;
        }

        int idx = (_bufferStartIdx + _bufferNumUsed++) % BufferSize;

        if (isFromKbd && (_configByte & (1 << 6)) != 0) {
            _buffer[idx].Data = GetTranslated(value);
        } else {
            _buffer[idx].Data = value;
        }
        _buffer[idx].IsFromAux = isFromAux;
        _buffer[idx].IsFromKbd = isFromKbd;
        _buffer[idx].SkipDelay = skipDelay || (!isFromAux && !isFromKbd);

        if (isFromAux) {
            ++_waitingBytesFromAux;
        }
        if (isFromKbd) {
            ++_waitingBytesFromKbd;
        }

        MaybeTransferBuffer();
    }

    private void BufferAddAux(byte value, bool skipDelay = false) {
        const bool isFromAux = true;
        const bool isFromKbd = false;

        BufferAdd(value, isFromAux, isFromKbd, skipDelay);
    }

    private void BufferAddKbd(byte value) {
        const bool isFromAux = false;
        const bool isFromKbd = true;

        BufferAdd(value, isFromAux, isFromKbd);
    }

    private byte GetInputPort() { // aka port P1
        byte port = 0b1010_0000;

        // bit 0: keyboard data in, ISA - unused
        // bit 1: mouse data in, ISA - unused
        // bit 2: ISA, EISA, PS/2 - unused
        //        MCA - 0 = keyboard has power, 1 = no power
        //        might be configured for clock switching
        // bit 3: ISA, EISA, PS/2 - unused
        //        might be configured for clock switching

        // bit 4: 0 = 512 KB, 1 = 256 KB
        // bit 5: 0 = manufacturer jumper, infinite diagnostics loop
        // bit 6: 0 = CGA, 1 = MDA
        // bit 7: 0 = keyboard locked, 1 = not locked

        // Simplified: assume CGA and not locked
        return port;
    }

    private byte GetOutputPort() { // aka port P2
        byte port = 0b0000_0001;

        // bit 0: 0 = CPU reset, 1 = normal
        // bit 1: 0 = A20 disabled, 1 = enabled
        if (_a20Gate.IsEnabled) {
            port |= (1 << 1);
        }
        // bit 2: mouse data out, ISA - unused
        // bit 3: mouse clock, ISA - unused

        // bit 4: 0 = IRQ1 (keyboard) not active, 1 = active
        if ((_configByte & (1 << 0)) != 0) {
            port |= (1 << 4);
        }
        // bit 5: 0 = IRQ12 (mouse) not active, 1 = active
        if ((_configByte & (1 << 1)) != 0) {
            port |= (1 << 5);
        }
        // bit 6: keyboard clock
        // bit 7: keyboard data out

        return port;
    }

    private static bool IsCmdMemRead(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0x20 and <= 0x3f;
    }

    private static bool IsCmdMemWrite(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0x60 and <= 0x7f;
    }

    private static bool IsCmdPulseLine(KeyboardCommand command) {
        byte code = (byte)command;
        return (code >= 0xf0);
    }

    private static bool IsCmdVendorLines(KeyboardCommand command) {
        byte code = (byte)command;
        return code is >= 0xb0 and <= 0xbd;
    }

    private void ExecuteCommand(KeyboardCommand command) {
        switch (command) {
            //
            // Commands requiring a parameter
            //
            case KeyboardCommand.WriteByteConfig:  // 0x60
            case KeyboardCommand.WriteOutputPort:  // 0xd1
            case KeyboardCommand.SimulateInputKbd: // 0xd2
            case KeyboardCommand.SimulateInputAux: // 0xd3
            case KeyboardCommand.WriteAux:         // 0xd4
                _currentCommand = command;
                break;
            case KeyboardCommand.WriteControllerMode: // 0xcb
                WarnControllerMode();
                _currentCommand = command;
                break;

            //
            // No-parameter commands
            //
            case KeyboardCommand.ReadByteConfig: // 0x20
                // Reads the keyboard controller configuration byte
                FlushBuffer();
                BufferAdd(_configByte);
                break;
            case (KeyboardCommand)0xa0: // ReadFwCopyright
                // Reads the keyboard controller firmware
                // copyright string, terminated by NUL
                FlushBuffer();
                foreach (char c in FirmwareCopyright) {
                    BufferAdd((byte)c);
                }
                BufferAdd(0);
                break;
            case (KeyboardCommand)0xa1: // ReadFwRevision
                // Reads the keyboard controller firmware
                // revision, always one byte
                FlushBuffer();
                BufferAdd(FirmwareRevision);
                break;
            case (KeyboardCommand)0xa4: // PasswordCheck
                // Check if password installed
                // 0xf1: not installed, or no hardware support
                // 0xfa: password installed
                FlushBuffer();
                BufferAdd(0xf1);
                break;
            case KeyboardCommand.DisablePortAux: // 0xa7
                // Disable aux (mouse) port
                _configByte |= (1 << 5);
                break;
            case KeyboardCommand.EnablePortAux: // 0xa8
                // Enable aux (mouse) port
                _configByte &= (byte)(_configByte | ~1 << 5); // safe as constant removal, equivalent to 0xDF
                break;
            case KeyboardCommand.TestPortAux: // 0xa9
                // Port test. Possible results:
                // 0x01: clock line stuck low
                // 0x02: clock line stuck high
                // 0x03: data line stuck low
                // 0x04: data line stuck high
                // Disables the aux (mouse) port
                _configByte |= (1 << 5); // disable aux
                FlushBuffer();
                BufferAdd(0x00);
                break;
            case KeyboardCommand.TestController: // 0xaa
                // Controller test. Possible results:
                // 0x55: passed; 0xfc: failed
                // Disables aux (mouse) and keyboard ports, enables translation,
                // enables A20 line, marks self-test as passed.
                _a20Gate.IsEnabled = true;
                _configByte |= (1 << 5); // disable aux
                _configByte |= (1 << 4); // disable kbd
                _configByte |= (1 << 6); // enable translation
                _configByte |= (1 << 2); // mark self-test passed
                FlushBuffer();
                BufferAdd(0x55);
                break;
            case KeyboardCommand.TestPortKbd: // 0xab
                // Port test. Possible results:
                // (as with aux port test)
                // Disables the keyboard port
                _configByte |= (1 << 4); // disable kbd
                FlushBuffer();
                BufferAdd(0x00); // as with TestPortAux
                break;
            case KeyboardCommand.DiagnosticDump: // 0xac
                // Dump the whole controller internal RAM (16 bytes),
                // output port, input port, test input, and status byte
                WarnInternalRamAccess();
                FlushBuffer();
                _isDiagnosticDump = true;
                DiagDumpByte(_configByte);
                for (byte idx = 1; idx <= 16; idx++) {
                    DiagDumpByte(0);
                }
                DiagDumpByte(GetInputPort());
                DiagDumpByte(GetOutputPort());
                WarnReadTestInputs();
                DiagDumpByte(0); // test input - TODO: not emulated for now
                DiagDumpByte(_statusByte);
                break;
            case KeyboardCommand.DisablePortKbd: // 0xad
                // Disable keyboard port; any keyboard command
                // reenables the port
                _configByte |= (1 << 4);
                break;
            case KeyboardCommand.EnablePortKbd: // 0xae
                // Enable the keyboard port
                _configByte &= 0xEF; // (0xEF = ~(1 << 4))
                break;
            case KeyboardCommand.ReadKeyboardVersion: // 0xaf
                // Reads the keyboard version
                // TODO: not found any meaningful description,
                // so the code follows 86Box behaviour
                FlushBuffer();
                BufferAdd(0);
                break;
            case KeyboardCommand.ReadInputPort: // 0xc0
                // Reads the controller input port (P1)
                FlushBuffer();
                BufferAdd(GetInputPort());
                break;
            case KeyboardCommand.ReadControllerMode: // 0xca
                // Reads keyboard controller mode
                // 0x00: ISA (AT)
                // 0x01: PS/2 (MCA)
                FlushBuffer();
                BufferAdd(0x01);
                break;
            case KeyboardCommand.ReadOutputPort: // 0xd0
                // Reads the controller output port (P2)
                FlushBuffer();
                BufferAdd(GetOutputPort());
                break;
            case KeyboardCommand.DisableA20: // 0xdd
                // Disable A20 line
                _a20Gate.IsEnabled = false;
                break;
            case KeyboardCommand.EnableA20: // 0xdf
                // Enable A20 line
                _a20Gate.IsEnabled = true;
                break;
            case KeyboardCommand.ReadTestInputs: // 0xe0
                // Read test bits:
                // bit 0: keyboard clock in
                // bit 1: (AT) keyboard data in, or (PS/2) mouse clock in
                // Not fully implemented
                WarnReadTestInputs();
                FlushBuffer();
                BufferAdd(0x00);
                break;
            //
            // Unknown or mostly unsupported commands
            //
            default:
                if (IsCmdMemRead(command)) { // 0x20-0x3f
                    // Read internal RAM - dummy, unimplemented
                    WarnInternalRamAccess();
                    BufferAdd(0x00);
                } else if (IsCmdMemWrite(command)) { // 0x60-0x7f
                    // Write internal RAM - dummy, unimplemented
                    WarnInternalRamAccess();
                    // requires a parameter
                    _currentCommand = command;
                } else if (IsCmdVendorLines(command)) { // 0xb0-0xbd
                    WarnVendorLines();
                } else if (IsCmdPulseLine(command)) { // 0xf0-0xff
                    // requires a parameter
                    _currentCommand = command;
                } else {
                    WarnUnknownCommand(command);
                }
                break;
        }
    }

    private void ExecuteCommand(KeyboardCommand command, byte param) {
        switch (command) {
            case KeyboardCommand.WriteByteConfig: // 0x60
                // Writes the keyboard controller configuration byte
                _configByte = param;
                // Force passed self-test bit and clear reserved bits
                _configByte |= (1 << 2);
                _configByte &= 0xF7; // (0xF7 = ~(1 << 3))
                _configByte &= 0x7F; // (0x7F = ~(1 << 7))
                break;
            case KeyboardCommand.WriteControllerMode: // 0xcb
                // Changes controller mode to PS/2 or AT
                // TODO: not implemented for now
                // ReadControllerMode will always claim PS/2
                break;
            case KeyboardCommand.WriteOutputPort: // 0xd1
                // Writes the controller output port (P2)
                _a20Gate.IsEnabled = (param & (1 << 1)) != 0;
                if ((param & (1 << 0)) == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("I8042: Clearing P2 bit 0 locks a real PC");
                    }
                    // System restart is not suported.
                }
                break;
            case KeyboardCommand.SimulateInputKbd: // 0xd2
                // Acts as if the byte was received from keyboard
                FlushBuffer();
                BufferAddKbd(param);
                break;
            case KeyboardCommand.SimulateInputAux: // 0xd3
                // Acts as if the byte was received from aux (mouse)
                FlushBuffer();
                BufferAddAux(param);
                break;
            case KeyboardCommand.WriteAux: // 0xd4
                // Sends a byte to the mouse.
                RestartDelayTimer(PortDelayMs * 2); // 'round trip' delay
                _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))
                // TODO: Mouse support
                // _statusByte |= (1 << 6) * (!MOUSEPS2_PortWrite(param));
                break;
            default:
                if (IsCmdMemWrite(command)) { // 0x60-0x7f
                    // Internal controller memory write,
                    // not implemented for most bytes
                } else if (IsCmdPulseLine(command)) { // 0xf0-0xff
                    // Pulse controller lines for 6ms,
                    // bits 0-3 counts, 0 = pulse relevant line
                    byte lines = (byte)(param & 0b0000_1111);
                    byte code = (byte)command;
                    if ((code == 0xf0 && param != 0b1111 && param != 0b1110) ||
                        (code != 0xf0 && param != 0b1111)) {
                        WarnLinePulse();
                    }
                    if (code == 0xf0 && (lines & 0b0001) == 0) {
                        if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                            _loggerService.Warning("System Reset is not implemented");
                        }
                    }
                } else {
                    // If we are here, than either this function
                    // was wrongly called or it is incomplete
                    throw new NotImplementedException("Keyhboard controller: unexpected command!");
                }
                break;
        }
    }

    private void DiagDumpByte(byte value) {
        // Based on communication logs collected from real chip
        // by Vogons forum user 'migry'

        int nibbleHi = (value & 0b1111_0000) >> 4;
        int nibbleLo = (value & 0b0000_1111);

        byte[] translationTable = {
            0x0b, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0a, 0x1e, 0x30, 0x2e, 0x20, 0x12, 0x21
        };

        // Diagnostic dumps send 3 bytes for each byte from memory:
        // - high nibble in hex ASCII, translated using codeset 1 table
        // - low nibble, similarly
        // - 0x39 (space in codeset 1)
        BufferAdd(translationTable[nibbleHi]);
        BufferAdd(translationTable[nibbleLo]);
        BufferAdd(0x39);
    }

    // ***************************************************************************
    // I/O port handlers
    // ***************************************************************************

    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data: // port 0x60
                if ((_statusByte & (1 << 0)) == 0) {
                    // Byte already read - just return the previous one
                    return _dataByte;
                }

                if (_isDiagnosticDump && _bufferNumUsed == 0) {
                    // Diagnostic dump finished
                    _isDiagnosticDump = false;
                    if (IsReadyForAuxFrame()) {
                        // MOUSEPS2_NotifyReadyForFrame(); // TODO: Mouse support
                    }
                    if (IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                }

                bool isDataFromAux = (_statusByte & (1 << 5)) != 0;
                bool isDataFromKbd = !isDataFromAux;

                if (isDataFromAux) {
                    if (_waitingBytesFromAux > 0) {
                        --_waitingBytesFromAux;
                    }
                    if (IsReadyForAuxFrame()) {
                        // MOUSEPS2_NotifyReadyForFrame(); // TODO: Mouse support
                    }
                }

                if (isDataFromKbd) {
                    if (_waitingBytesFromKbd > 0) {
                        --_waitingBytesFromKbd;
                    }
                    if (IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                }

                byte retVal = _dataByte;

                _statusByte &= 0xFE; // mark byte as already read (0xFE = ~(1 << 0))
                _statusByte &= 0xDF; // clear aux flag (0xDF = ~(1 << 5))

                // Enforce the simulated data transfer delay, as some software
                // (Tyrian 2000 setup) reads the port without waiting for the
                // interrupt.
                RestartDelayTimer(PortDelayMs);

                return retVal;

            case KeyboardPorts.StatusRegister: // port 0x64
                return _statusByte;

            default:
                return base.ReadByte(port);
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case KeyboardPorts.Data: // port 0x60
                _statusByte &= 0xF7; // was_last_write_cmd = false (0xF7 = ~(1 << 3))

                if (_currentCommand != KeyboardCommand.None) {
                    // A controller command is waiting for a parameter
                    KeyboardCommand command = _currentCommand;
                    _currentCommand = KeyboardCommand.None;

                    bool shouldNotifyAux = !IsReadyForAuxFrame();
                    bool shouldNotifyKbd = !IsReadyForKbdFrame();

                    _shouldSkipDeviceNotify = true;
                    FlushBuffer();
                    ExecuteCommand(command, value);
                    _shouldSkipDeviceNotify = false;

                    if (shouldNotifyAux && IsReadyForAuxFrame()) {
                        // MOUSEPS2_NotifyReadyForFrame(); // TODO: Mouse support
                    }
                    if (shouldNotifyKbd && IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                } else {
                    // Send this byte to the keyboard
                    _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))
                    _configByte &= 0xEF; // auto-enable keyboard port (0xEF = ~(1 << 4))

                    FlushBuffer();
                    RestartDelayTimer(PortDelayMs * 2); // 'round trip' delay
                    KeyboardDevice.PortWrite(value);
                }
                break;

            case KeyboardPorts.Command: // port 0x64
                _shouldSkipDeviceNotify = true;

                bool shouldNotifyAux2 = !IsReadyForAuxFrame();
                bool shouldNotifyKbd2 = !IsReadyForKbdFrame();

                if (_isDiagnosticDump) {
                    _isDiagnosticDump = false;
                    FlushBuffer();
                }

                _statusByte |= (1 << 3); // was_last_write_cmd = true

                _currentCommand = KeyboardCommand.None;
                if (value is <= 0x1f or >= 0x40 and <= 0x5f) {
                    // AMI BIOS systems command aliases
                    ExecuteCommand((KeyboardCommand)(value + 0x20));
                } else {
                    ExecuteCommand((KeyboardCommand)value);
                }

                _shouldSkipDeviceNotify = false;

                if (shouldNotifyAux2 && IsReadyForAuxFrame()) {
                    // MOUSEPS2_NotifyReadyForFrame(); // TODO: Mouse support
                }
                if (shouldNotifyKbd2 && IsReadyForKbdFrame()) {
                    KeyboardDevice.NotifyReadyForFrame();
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    // ***************************************************************************
    // External entry points
    // ***************************************************************************

    /// <summary>
    /// Adds a single byte from auxiliary device (mouse).
    /// </summary>
    /// <param name="value">The byte to add.</param>
    public void AddAuxByte(byte value) {
        if ((_configByte & (1 << 5)) != 0) {
            return; // aux (mouse) port is disabled
        }

        _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))

        EnforceBufferSpace();
        BufferAddAux(value);
    }

    /// <summary>
    /// Adds a frame of bytes from auxiliary device (mouse).
    /// </summary>
    /// <param name="bytes">The bytes to add.</param>
    public void AddAuxFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || (_configByte & (1 << 5)) != 0) {
            return; // empty frame or aux (mouse) port is disabled
        }

        _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))

        // Cheat a little to improve input latency - skip delay timer between
        // subsequent bytes of mouse data frame; this seems to be compatible
        // with all the PS/2 mouse drivers tested so far.

        bool skipDelay = false;
        EnforceBufferSpace(bytes.Count);
        foreach (byte b in bytes) {
            BufferAddAux(b, skipDelay);
            skipDelay = true;
        }
    }

    /// <summary>
    /// Adds a single byte from keyboard.
    /// </summary>
    /// <param name="value">The byte to add.</param>
    internal void AddKbdByte(byte value) {
        if ((_configByte & (1 << 4)) != 0) {
            return; // keyboard port is disabled
        }

        _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))

        EnforceBufferSpace();
        BufferAddKbd(value);
    }

    /// <summary>
    /// Adds a frame of bytes from keyboard.
    /// </summary>
    /// <param name="bytes">The bytes to add.</param>
    internal void AddKbdFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || (_configByte & (1 << 4)) != 0) {
            return; // empty frame or keyboard port is disabled
        }

        _statusByte &= 0xBF; // clear timeout flag (0xBF = ~(1 << 6))

        EnforceBufferSpace(bytes.Count);
        foreach (byte b in bytes) {
            BufferAddKbd(b);
        }
    }

    /// <summary>
    /// Checks if the controller is ready to accept auxiliary frames.
    /// </summary>
    /// <returns>True if ready for auxiliary frames.</returns>
    internal bool IsReadyForAuxFrame() {
        return _waitingBytesFromAux == 0 && (_configByte & (1 << 5)) == 0 && !_isDiagnosticDump;
    }

    /// <summary>
    /// Checks if the controller is ready to accept keyboard frames.
    /// </summary>
    /// <returns>True if ready for keyboard frames.</returns>
    internal bool IsReadyForKbdFrame() {
        return _waitingBytesFromKbd == 0 && (_configByte & (1 << 4)) == 0 && !_isDiagnosticDump;
    }
}