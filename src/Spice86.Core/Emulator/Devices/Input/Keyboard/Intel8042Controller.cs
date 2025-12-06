namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Emulates the Intel 8042 keyboard controller, providing PS/2 keyboard and mouse (auxiliary device) port functionality
/// for x86 system emulation.
/// </summary>
public partial class Intel8042Controller : DefaultIOPortHandler {
    private const byte IrqNumKeyboard = 1;
    private const byte IrqNumMouse = 12;

    private const byte FirmwareRevision = 0x00;
    private const string FirmwareCopyright = nameof(Spice86);

    private const int BufferSize = 64; // in bytes
    /// <summary>
    /// delay appropriate for 20-30 kHz serial clock and 11 bits/byte
    /// </summary>
    private const double PortDelayMs = 0.300; // 0.3ms

    private readonly Queue<BufferEntry> _buffer = new(BufferSize);

    private int _waitingBytesFromAux = 0;
    private int _waitingBytesFromKbd = 0;

    /// <summary>
    /// Sub-ms delay state driven by PIC event timer
    /// </summary>
    private bool _isDelayRunning = false;

    /// <summary>
    /// Executing command, do not notify devices about readiness for accepting frame
    /// </summary>
    private bool _shouldSkipDeviceNotify = false;

    /// <summary>
    /// Command currently being executed, waiting for parameter
    /// </summary>
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    /// <summary>
    /// Byte 0x00 of the controller memory - configuration byte (wrapped for debugability)
    /// </summary>
    private readonly ConfigByte _config = new(0b00000111);

    /// <summary>
    /// Byte returned from port 0x60
    /// </summary>
    private byte _dataByte = 0;

    private readonly Status _status = new(0b00011100);

    /// <summary>
    /// If enabled, all keyboard events are dropped until dump is done
    /// </summary>
    private bool _isDiagnosticDump = false;

    private readonly bool[] _unknownCommandWarned = new bool[256];
    private bool _bufferFullWarned = false;
    private long _lastTimeStamp = 0;
    private bool _controllerModeWarned = false;
    private bool _internalRamWarned = false;
    private bool _linePulseWarned = false;
    private bool _readTestInputsWarned = false;
    private bool _vendorLinesWarned = false;

    private readonly A20Gate _a20Gate;
    private readonly DualPic _dualPic;
    private readonly State _cpuState;

    // Handler reference for delay timer
    private readonly EmulatedTimeEventHandler _delayHandler;

    public PS2Keyboard KeyboardDevice { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Intel8042Controller"/> class.
    /// </summary>
    public Intel8042Controller(State state, IOPortDispatcher ioPortDispatcher,
        A20Gate a20Gate, DualPic dualPic, bool failOnUnhandledPort,
        IPauseHandler pauseHandler, ILoggerService loggerService,
        IGuiKeyboardEvents? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        _cpuState = state;

        // Initialize handler reference
        _delayHandler = DelayExpireHandler;

        KeyboardDevice = new PS2Keyboard(this, dualPic, loggerService, gui);

        InitPortHandlers(ioPortDispatcher);
        FlushBuffer();
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Command, this);
    }

    private void LogWarnBufferIsFull() {
        const uint thresoldCycles = 150;

        if (!_bufferFullWarned || (_cpuState.Cycles - _lastTimeStamp > thresoldCycles)) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Internal buffer overflow");
            }
            _lastTimeStamp = _cpuState.Cycles;
            _bufferFullWarned = true;
        }
    }

    private void LogWarnSwitchToATMode() {
        if (!_controllerModeWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Switching controller to AT mode not emulated");
            }
            _controllerModeWarned = true;
        }
    }

    private void LogWarnAccessToInternalRam() {
        if (!_internalRamWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Accessing internal RAM (other than byte 0x00) gives vendor-specific results");
            }
            _internalRamWarned = true;
        }
    }

    private void LogWarnPulseOtherThanResetNotImplemented() {
        if (!_linePulseWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Pulsing line other than RESET not emulated");
            }
            _linePulseWarned = true;
        }
    }

    private void LogWarnReadTestInputsNotImplemented() {
        if (!_readTestInputsWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Reading test inputs not implemented");
            }
            _readTestInputsWarned = true;
        }
    }

    private void LogWarnVendorLineCommandsNotImplemented() {
        if (!_vendorLinesWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: No vendor-specific commands to manipulate controller lines are emulated");
            }
            _vendorLinesWarned = true;
        }
    }

    private void LogWarnUnknownCommand(KeyboardCommand command) {
        byte code = (byte)command;
        if (!_unknownCommandWarned[code]) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Unknown command 0x{Command:X2}", code);
            }
            _unknownCommandWarned[code] = true;
        }
    }

    /// <summary>
    /// Translates a keyboard input byte from scancode set 2 (IBM XT) to the equivalent scancode set 1 value.
    /// </summary>
    /// <remarks>This method is intended to provide compatibility for software that only recognizes scancode
    /// set 1 by converting all incoming keyboard bytes, including both scancodes and command responses, from set 2. The
    /// translation is based on a fixed lookup table commonly used in keyboard emulation.</remarks>
    /// <param name="value">The input byte representing a scancode or command response from the keyboard, using scancode set 2.</param>
    /// <returns>A byte representing the translated value in scancode set 1 format.</returns>
    private static byte GetTranslated(byte value) {
        // A brain damaged keyboard input translation

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
        bool isDataFromAux = _status.IsDataFromAux;
        bool isDataFromKbd = !isDataFromAux && _status.IsDataPending;

        if (isDataFromAux && _config.MouseIrqEnabled) {
            _dualPic.ProcessInterruptRequest(IrqNumMouse);
        }
        if (isDataFromKbd && _config.KbdIrqEnabled) {
            _dualPic.ProcessInterruptRequest(IrqNumKeyboard);
        }
    }

    private void FlushBuffer() {
        _status.IsDataPending = false;
        _status.IsDataFromAux = false;

        _buffer.Clear();

        bool shouldNotifyAux = !_shouldSkipDeviceNotify && !IsReadyForAuxFrame;
        bool shouldNotifyKbd = !_shouldSkipDeviceNotify && !IsReadyForKbdFrame;

        _waitingBytesFromAux = 0;
        _waitingBytesFromKbd = 0;

        if (shouldNotifyAux && IsReadyForAuxFrame) {
            // TODO: Mouse notify-ready, someday...
        }

        if (shouldNotifyKbd && IsReadyForKbdFrame) {
            KeyboardDevice.NotifyReadyForFrame();
        }
    }

    /// <summary>
    /// Ensures that the buffer has sufficient space to accommodate the specified number of bytes, flushing the buffer
    /// if necessary.
    /// </summary>
    /// <param name="numBytes">The number of bytes that must fit in the buffer. Must be less than or equal to the buffer size. The default is
    /// 1.</param>
    private void EnforceBufferSpace(int numBytes = 1) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numBytes, BufferSize);

        if (BufferSize < _buffer.Count + numBytes) {
            LogWarnBufferIsFull();
            FlushBuffer();
        }
    }

    private void RestartDelayTimer(double timeMs = PortDelayMs) {
        if (_isDelayRunning) {
            _dualPic.RemoveEvents(_delayHandler);
        }
        _dualPic.AddEvent(_delayHandler, timeMs);
        _isDelayRunning = true;
        IsDelayExpired = false;
    }

    private void DelayExpireHandler(uint _) {
        _isDelayRunning = false;
        IsDelayExpired = true;
        TransferNextBufferedByteIfReady();
    }

    private bool IsDelayExpired { get; set; } = true;

    /// <summary>
    /// Transfers the next buffered byte to the output port if the buffer is not empty and any required delay has
    /// expired.
    /// </summary>
    private void TransferNextBufferedByteIfReady() {
        if (_status.IsDataPending || _buffer.Count == 0) {
            // There is already some data waiting to be picked up,
            // or there is nothing waiting in the buffer
            return;
        }

        // If not set to skip the delay, do not send byte until timer expires
        BufferEntry entry = _buffer.Peek();
        if (!IsDelayExpired && !entry.SkipDelay) {
            return;
        }

        // Mark byte as consumed
        _buffer.Dequeue();

        // Transfer one byte of data from buffer to output port
        _dataByte = entry.Data;
        _status.IsDataFromAux = entry.IsFromAux;
        _status.IsDataPending = true;
        RestartDelayTimer();
        ActivateIrqsIfNeeded();
    }

    private void BufferAdd(byte value, bool isFromAux = false,
        bool isFromKbd = false, bool skipDelay = false) {
        if ((isFromAux && _config.IsAuxPortDisabled) ||
            (isFromKbd && _config.IsKeyboardPortDisabled)) {
            // Byte came from a device which is currently disabled
            return;
        }

        if (_buffer.Count >= BufferSize) {
            LogWarnBufferIsFull();
            FlushBuffer();
            return;
        }

        var entry = new BufferEntry();
        if ((isFromKbd && _config.TranslationEnabled)) {
            entry.Data = GetTranslated(value);
        } else {
            entry.Data = value;
        }
        entry.IsFromAux = isFromAux;
        entry.IsFromKbd = isFromKbd;
        entry.SkipDelay = skipDelay || (!isFromAux && !isFromKbd);
        _buffer.Enqueue(entry);

        if (isFromAux) {
            ++_waitingBytesFromAux;
        }
        if (isFromKbd) {
            ++_waitingBytesFromKbd;
        }

        TransferNextBufferedByteIfReady();
    }

    private void BufferAddAuxByte(byte value, bool skipDelay = false) => BufferAdd(value, true, false, skipDelay);
    private void BufferAddKbd(byte value) => BufferAdd(value, false, true);

    private byte GetInputPort() { // aka port P1
        byte port = 0b1010_0000;
        return port;
    }

    private byte GetOutputPort() { // aka port P2
        byte port = (byte)OutputPortBits.ResetNotAsserted;

        // bit 0: 0 = CPU reset, 1 = normal
        // bit 1: 0 = A20 disabled, 1 = enabled
        if (_a20Gate.IsEnabled) {
            port |= (byte)OutputPortBits.A20Enabled;
        }
        // bit 2: mouse data out, ISA - unused
        // bit 3: mouse clock, ISA - unused

        // bit 4: 0 = IRQ1 (keyboard) not active, 1 = active
        if (_config.KbdIrqEnabled) {
            port |= (byte)OutputPortBits.KeyboardIrqActive;
        }
        // bit 5: 0 = IRQ12 (mouse) not active, 1 = active
        if (_config.MouseIrqEnabled) {
            port |= (byte)OutputPortBits.MouseIrqActive;
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
                LogWarnSwitchToATMode();
                _currentCommand = command;
                break;

            //
            // No-parameter commands
            //
            case KeyboardCommand.ReadByteConfig: // 0x20
                // Reads the keyboard controller configuration byte
                FlushBuffer();
                BufferAdd(_config.ToByte());
                break;
            case (KeyboardCommand)0xa0: // ReadFwCopyright
                // Reads the keyboard controller firmware
                // copyright string, terminated by NUL
                FlushBuffer();
                foreach (char c in FirmwareCopyright) {
                    BufferAdd((byte)c);
                }
                BufferAdd((byte)Response.Ok);
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
                BufferAdd((byte)Response.PasswordNotInstalled);
                break;
            case KeyboardCommand.DisablePortAux: // 0xa7
                // Disable aux (mouse) port
                _config.IsAuxPortDisabled = true;
                break;
            case KeyboardCommand.EnablePortAux: // 0xa8
                // Enable aux (mouse) port
                _config.IsAuxPortDisabled = false;
                break;
            case KeyboardCommand.TestPortAux: // 0xa9
                // Port test. Possible results:
                // 0x01: clock line stuck low
                // 0x02: clock line stuck high
                // 0x03: data line stuck low
                // 0x04: data line stuck high
                // Disables the aux (mouse) port
                _config.IsAuxPortDisabled = true; // disable aux
                FlushBuffer();
                BufferAdd((byte)Response.Ok);
                break;
            case KeyboardCommand.TestController: // 0xaa
                // Controller test. Possible results:
                // 0x55: passed; 0xfc: failed
                // Disables aux (mouse) and keyboard ports, enables translation,
                // enables A20 line, marks self-test as passed.
                _a20Gate.IsEnabled = true;
                _config.IsAuxPortDisabled = true;  // disable aux
                _config.IsKeyboardPortDisabled = true;  // disable kbd
                _config.TranslationEnabled = true; // enable translation
                _config.SelfTestPassed = true;  // mark self-test passed
                FlushBuffer();
                BufferAdd((byte)Response.SelfTestPassed);
                break;
            case KeyboardCommand.TestPortKbd: // 0xab
                // Port test. Possible results:
                // (as with aux port test)
                // Disables the keyboard port
                _config.IsKeyboardPortDisabled = true; // disable kbd
                FlushBuffer();
                BufferAdd((byte)Response.Ok); // as with TestPortAux
                break;
            case KeyboardCommand.DiagnosticDump: // 0xac
                // Dump the whole controller internal RAM (16 bytes),
                // output port, input port, test input, and status byte
                LogWarnAccessToInternalRam();
                FlushBuffer();
                _isDiagnosticDump = true;
                DiagDumpByte(_config.ToByte());
                for (byte idx = 1; idx <= 16; idx++) {
                    DiagDumpByte(0);
                }
                DiagDumpByte(GetInputPort());
                DiagDumpByte(GetOutputPort());
                LogWarnReadTestInputsNotImplemented();
                DiagDumpByte(_status.ToByte());
                break;
            case KeyboardCommand.DisablePortKbd: // 0xad
                // Disable keyboard port
                // any keyboard command reenables the port
                _config.IsKeyboardPortDisabled = true;
                break;
            case KeyboardCommand.EnableKeyboardPort: // 0xae
                // Enable the keyboard port
                _config.IsKeyboardPortDisabled = false;
                break;
            case KeyboardCommand.ReadKeyboardVersion: // 0xaf
                // Reads the keyboard version
                // TODO: not found any meaningful description,
                // so the code follows 86Box behaviour
                FlushBuffer();
                BufferAdd((byte)Response.Ok);
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
                BufferAdd((byte)ControllerModeValue.Ps2Mca);
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
                LogWarnReadTestInputsNotImplemented();
                FlushBuffer();
                BufferAdd((byte)Response.Ok);
                break;
            //
            // Unknown or mostly unsupported commands
            //
            default:
                if (IsCmdMemRead(command)) { // 0x20-0x3f
                    // Read internal RAM - dummy, unimplemented
                    LogWarnAccessToInternalRam();
                    BufferAdd((byte)Response.Ok);
                } else if (IsCmdMemWrite(command)) { // 0x60-0x7f
                    // Write internal RAM - dummy, unimplemented
                    LogWarnAccessToInternalRam();
                    // requires a parameter
                    _currentCommand = command;
                } else if (IsCmdVendorLines(command)) { // 0xb0-0xbd
                    LogWarnVendorLineCommandsNotImplemented();
                } else if (IsCmdPulseLine(command)) { // 0xf0-0xff
                    // requires a parameter
                    _currentCommand = command;
                } else {
                    LogWarnUnknownCommand(command);
                }
                break;
        }
    }

    /// <summary>
    /// Executes a keyboard controller command with the specified parameter byte.
    /// </summary>
    /// <param name="command">The keyboard controller command to execute. Determines the operation to perform.</param>
    /// <param name="param">A parameter byte whose meaning depends on the specified command. For example, it may represent configuration
    /// data or output port values.</param>
    /// <exception cref="NotImplementedException">Thrown if the specified command is not recognized or not supported by the controller.</exception>
    private void ExecuteCommand(KeyboardCommand command, byte param) {
        switch (command) {
            case KeyboardCommand.WriteByteConfig: // 0x60
                // Writes the keyboard controller configuration byte
                _config.FromByte(param);
                // Force passed self-test bit and clear reserved bits
                _config.SelfTestPassed = true; // bit2
                _config.Reserved3 = false;     // bit3
                _config.Reserved7 = false;     // bit7
                break;
            case KeyboardCommand.WriteControllerMode: // 0xcb
                // Changes controller mode to PS/2 or AT
                // TODO: not implemented for now
                // ReadControllerMode will always claim PS/2
                break;
            case KeyboardCommand.WriteOutputPort: // 0xd1
                // Writes the controller output port (P2)
                _a20Gate.IsEnabled = (param & (byte)OutputPortBits.A20Enabled) != 0;
                if ((param & (byte)OutputPortBits.ResetNotAsserted) == 0 && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                    _loggerService.Warning("I8042: Clearing P2 bit 0 locks a real PC");
                    // System restart is not supported.
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
                BufferAddAuxByte(param);
                break;
            case KeyboardCommand.WriteAux: // 0xd4
                RestartDelayTimer(PortDelayMs * 2);
                _status.Timeout = false;
                // TODO: mouse port write and timeout flag, someday...
                break;
            default:
                if (IsCmdMemWrite(command)) {
                    // ignored
                } else if (IsCmdPulseLine(command)) {
                    byte lines = (byte)(param & (byte)LineParam.AllLines);
                    byte code = (byte)command;
                    if ((code == 0xf0 && param != (byte)LineParam.AllLines && param != (byte)LineParam.AllLinesExceptReset) ||
                        (code != 0xf0 && param != (byte)LineParam.AllLines)) {
                        LogWarnPulseOtherThanResetNotImplemented();
                    }
                    if (code == 0xf0 && (lines & (byte)LineParam.Reset) == 0 && _loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("System Reset is not implemented");
                    }
                } else {
                    // If we are here, than either this function
                    // was wrongly called or it is incomplete
                    throw new NotImplementedException("Keyboard controller: unexpected command!");
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
        BufferAdd((byte)Response.SpaceScanCodeSet1);
    }

    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data: // port 0x60
                if (!_status.IsDataPending) {
                    // Byte already read - just return the previous one
                    return _dataByte;
                }

                if (_isDiagnosticDump && _buffer.Count == 0) {
                    // Diagnostic dump finished
                    _isDiagnosticDump = false;
                    if (IsReadyForAuxFrame) {
                        // TODO: mouse notify-ready
                    }
                    if (IsReadyForKbdFrame) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                }

                bool isDataFromAux = _status.IsDataFromAux;
                bool isDataFromKbd = !isDataFromAux;

                if (isDataFromAux) {
                    if (_waitingBytesFromAux > 0) {
                        --_waitingBytesFromAux;
                    }
                    if (IsReadyForAuxFrame) {
                        // TODO: mouse notify-ready, someday...
                    }
                }

                if (isDataFromKbd) {
                    if (_waitingBytesFromKbd > 0) {
                        --_waitingBytesFromKbd;
                    }
                    if (IsReadyForKbdFrame) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                }

                byte retVal = _dataByte;

                _status.IsDataPending = false;  // mark byte as already read
                _status.IsDataFromAux = false; // clear aux flag

                // Enforce the simulated data transfer delay, as some software
                // (Tyrian 2000 setup) reads the port without waiting for the
                // interrupt.
                RestartDelayTimer(PortDelayMs);

                return retVal;

            case KeyboardPorts.StatusRegister: // port 0x64
                return _status.ToByte();

            default:
                return base.ReadByte(port);
        }
    }

    public override void WriteByte(ushort port, byte value) {
        switch (port) {
            case KeyboardPorts.Data: // port 0x60
                _status.WasLastWriteCmd = false;

                if (_currentCommand != KeyboardCommand.None) {
                    // A controller command is waiting for a parameter
                    KeyboardCommand command = _currentCommand;
                    _currentCommand = KeyboardCommand.None;

                    bool shouldNotifyAux = !IsReadyForAuxFrame;
                    bool shouldNotifyKbd = !IsReadyForKbdFrame;

                    _shouldSkipDeviceNotify = true;
                    FlushBuffer();
                    ExecuteCommand(command, value);
                    _shouldSkipDeviceNotify = false;

                    if (shouldNotifyAux && IsReadyForAuxFrame) {
                        // TODO: mouse notify-ready
                    }
                    if (shouldNotifyKbd && IsReadyForKbdFrame) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                } else {
                    // Send this byte to the keyboard
                    _status.Timeout = false;
                    // auto-enable keyboard port
                    _config.IsKeyboardPortDisabled = false;

                    FlushBuffer();
                    // 'round trip' delay
                    RestartDelayTimer(PortDelayMs * 2);
                    KeyboardDevice.PortWrite(value);
                }
                break;

            case KeyboardPorts.Command: // port 0x64
                _shouldSkipDeviceNotify = true;

                bool shouldNotifyAux2 = !IsReadyForAuxFrame;
                bool shouldNotifyKbd2 = !IsReadyForKbdFrame;

                if (_isDiagnosticDump) {
                    _isDiagnosticDump = false;
                    FlushBuffer();
                }

                _status.WasLastWriteCmd = true;

                _currentCommand = KeyboardCommand.None;
                if (value is <= 0x1f or >= 0x40 and <= 0x5f) {
                    // AMI BIOS systems command aliases
                    ExecuteCommand((KeyboardCommand)(value + 0x20));
                } else {
                    ExecuteCommand((KeyboardCommand)value);
                }

                _shouldSkipDeviceNotify = false;

                if (shouldNotifyAux2 && IsReadyForAuxFrame) {
                    // TODO: mouse notify-ready, someday...
                }
                if (shouldNotifyKbd2 && IsReadyForKbdFrame) {
                    KeyboardDevice.NotifyReadyForFrame();
                }
                break;

            default:
                base.WriteByte(port, value);
                break;
        }
    }

    /// <summary>
    /// Adds a single byte from auxiliary device (mouse).
    /// </summary>
    /// <param name="value">The byte to add.</param>
    public void AddAuxByte(byte value) {
        if (_config.IsAuxPortDisabled) {
            return; // aux (mouse) port is disabled
        }

        _status.Timeout = false;

        EnforceBufferSpace();
        BufferAddAuxByte(value);
    }

    /// <summary>
    /// Adds a frame of bytes from auxiliary device (mouse).
    /// </summary>
    /// <remarks>
    /// Not wired up. AUX port, mouse notification, and mouse frames are 'TODO'
    /// </remarks>
    /// <param name="bytes">The bytes to add.</param>
    public void AddAuxFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || _config.IsAuxPortDisabled) {
            return; // empty frame or aux (mouse) port is disabled
        }
        _status.Timeout = false;

        bool skipDelay = false;
        EnforceBufferSpace(bytes.Count);
        foreach (byte b in bytes) {
            BufferAddAuxByte(b, skipDelay);
            skipDelay = true;
        }
    }

    /// <summary>
    /// Adds a single byte from keyboard.
    /// </summary>
    /// <param name="value">The byte to add.</param>
    internal void AddKeyboardByte(byte value) {
        if (_config.IsKeyboardPortDisabled) {
            return; // keyboard port is disabled
        }

        _status.Timeout = false;

        EnforceBufferSpace();
        BufferAddKbd(value);
    }

    /// <summary>
    /// Adds a frame of bytes from keyboard.
    /// </summary>
    /// <param name="bytes">The bytes to add.</param>
    internal void AddKeyboardFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || _config.IsKeyboardPortDisabled) {
            return; // empty frame or keyboard port is disabled
        }

        _status.Timeout = false;

        EnforceBufferSpace(bytes.Count);
        foreach (byte b in bytes) {
            BufferAddKbd(b);
        }
    }

    /// <summary>
    /// Checks if the controller is ready to accept auxiliary frames.
    /// </summary>
    /// <returns>True if ready for auxiliary frames.</returns>
    internal bool IsReadyForAuxFrame => _waitingBytesFromAux == 0 && !_config.IsAuxPortDisabled && !_isDiagnosticDump;

    /// <summary>
    /// Checks if the controller is ready to accept keyboard frames.
    /// </summary>
    /// <returns>True if ready for keyboard frames.</returns>
    internal bool IsReadyForKbdFrame => _waitingBytesFromKbd == 0 && !_config.IsKeyboardPortDisabled && !_isDiagnosticDump;
}