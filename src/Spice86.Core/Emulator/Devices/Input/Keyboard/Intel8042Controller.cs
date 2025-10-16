namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;
using System.Diagnostics;

[DebuggerDisplay("I8042 New={_status.IsDataNew} Aux={_status.IsDataFromAux} Cmd={_status.WasLastWriteCmd} Buf={_bufferNumUsed}/{BufferSize} WaitK={_waitingBytesFromKbd} WaitM={_waitingBytesFromAux} DisKbd={_config.DisableKbdPort} CmdPending={_currentCommand}")]
public class Intel8042Controller : DefaultIOPortHandler {
    private const byte IrqNumKbdIbmPc = 1;
    private const byte IrqNumMouse = 12;

    private const byte FirmwareRevision = 0x00;
    private const string FirmwareCopyright = nameof(Spice86);

    private const int BufferSize = 64; // in bytes
    // delay appropriate for 20-30 kHz serial clock and 11 bits/byte
    private const double PortDelayMs = 0.300; // 0.3ms

    /// <summary>
    /// Common controller byte values used across 8042 operations.
    /// </summary>
    public enum Response : byte {
        /// <summary>
        /// Generic OK/zero value, used by port tests and various default reads.
        /// Also used as NUL terminator for the firmware copyright string.
        /// </summary>
        Ok = 0x00,
        /// <summary>
        /// Controller self-test passed value (result of command 0xAA).
        /// </summary>
        SelfTestPassed = 0x55,
        /// <summary>
        /// Password not installed or unsupported (command 0xA4 result).
        /// </summary>
        PasswordNotInstalled = 0xF1,
        /// <summary>
        /// Space key make code in Set 1 (used by diagnostic dump formatting).
        /// </summary>
        SpaceScanCodeSet1 = 0x39,
    }

    /// <summary>
    /// Output port (P2) bit definitions.
    /// </summary>
    [Flags]
    public enum OutputPortBits : byte {
        /// <summary>
        /// Bit 0: 1 = normal, 0 = CPU reset asserted.
        /// </summary>
        ResetNotAsserted = 1 << 0,
        /// <summary>
        /// Bit 1: A20 line enabled.
        /// </summary>
        A20Enabled = 1 << 1,
        /// <summary>
        /// Bit 4: Keyboard IRQ1 active.
        /// </summary>
        KeyboardIrqActive = 1 << 4,
        /// <summary>
        /// Bit 5: Mouse IRQ12 active.
        /// </summary>
        MouseIrqActive = 1 << 5,
    }

    /// <summary>
    /// Status register bit definitions.
    /// </summary>
    [Flags]
    public enum StatusBits : byte {
        /// <summary>
        /// Bit 0: Output buffer status (1 = data available).
        /// </summary>
        OutputBufferFull = 0x01,
        /// <summary>
        /// Bit 1: Input buffer status.
        /// </summary>
        InputBufferFull = 0x02,
        /// <summary>
        /// Bit 2: System flag.
        /// </summary>
        SystemFlag = 0x04,
        /// <summary>
        /// Bit 3: Last write was a command.
        /// </summary>
        LastWriteWasCommand = 0x08,
        /// <summary>
        /// Bit 4: Reserved/unused.
        /// </summary>
        Reserved4 = 0x10,
        /// <summary>
        /// Bit 5: Data came from auxiliary device (mouse).
        /// </summary>
        DataFromAux = 0x20,
        /// <summary>
        /// Bit 6: Timeout.
        /// </summary>
        Timeout = 0x40,
        /// <summary>
        /// Bit 7: Parity error.
        /// </summary>
        ParityError = 0x80,
    }

    /// <summary>
    /// Controller mode values.
    /// </summary>
    public enum ControllerModeValue : byte {
        /// <summary>
        /// ISA (AT) mode.
        /// </summary>
        IsaAt = 0x00,
        /// <summary>
        /// PS/2 (MCA) mode.
        /// </summary>
        Ps2Mca = 0x01,
    }

    /// <summary>
    /// Line parameter masks used by pulse-lines commands (0xF0-0xFF).
    /// </summary>
    [Flags]
    public enum LineParam : byte {
        /// <summary>
        /// Reset line mask (bit 0).
        /// </summary>
        Reset = 0b0001,
        /// <summary>
        /// All lines mask (bits 0..3 set).
        /// </summary>
        AllLines = 0b1111,
        /// <summary>
        /// All lines except Reset (bits 1..3 set).
        /// </summary>
        AllLinesExceptReset = 0b1110,
    }

    /// <summary>
    /// Configuration byte bit definitions.
    /// </summary>
    [Flags]
    public enum ConfigBits : byte {
        /// <summary>
        /// Bit 0: Keyboard IRQ enabled (IRQ1).
        /// </summary>
        KbdIrqEnabled = 1 << 0,
        /// <summary>
        /// Bit 1: Mouse IRQ enabled (IRQ12).
        /// </summary>
        MouseIrqEnabled = 1 << 1,
        /// <summary>
        /// Bit 2: Self-test passed.
        /// </summary>
        SelfTestPassed = 1 << 2,
        /// <summary>
        /// Bit 3: Reserved.
        /// </summary>
        Reserved3 = 1 << 3,
        /// <summary>
        /// Bit 4: Keyboard port disabled.
        /// </summary>
        DisableKbdPort = 1 << 4,
        /// <summary>
        /// Bit 5: Auxiliary (mouse) port disabled.
        /// </summary>
        DisableAuxPort = 1 << 5,
        /// <summary>
        /// Bit 6: Translation enabled (AT -&gt; XT).
        /// </summary>
        TranslationEnabled = 1 << 6,
        /// <summary>
        /// Bit 7: Reserved.
        /// </summary>
        Reserved7 = 1 << 7,
    }

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

    // Sub-ms delay state driven by Timer one-shots
    private bool _delayActive = false;
    private int _delayToken = 0; // invalidates prior scheduled delay expiry callbacks

    // Executing command, do not notify devices about readiness for accepting frame
    private bool _shouldSkipDeviceNotify = false;

    // Command currently being executed, waiting for parameter
    private KeyboardCommand _currentCommand = KeyboardCommand.None;

    // Byte 0x00 of the controller memory - configuration byte (wrapped for debugability)
    private readonly ConfigByte _config = new(0b00000111);

    // Byte returned from port 0x60
    private byte _dataByte = 0;

    private readonly Status _status = new(0b00011100);

    // If enabled, all keyboard events are dropped until dump is done
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
    private readonly DeviceScheduler _scheduler;

    public PS2Keyboard KeyboardDevice { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Intel8042Controller"/> class.
    /// </summary>
    public Intel8042Controller(State state, IOPortDispatcher ioPortDispatcher,
        A20Gate a20Gate, DualPic dualPic, bool failOnUnhandledPort,
        IPauseHandler pauseHandler, ILoggerService loggerService,
        DeviceScheduler scheduler, IGuiKeyboardEvents? gui = null)
        : base(state, failOnUnhandledPort, loggerService) {
        _a20Gate = a20Gate;
        _dualPic = dualPic;
        _cpuState = state;
        _scheduler = scheduler;

        KeyboardDevice = new PS2Keyboard(this, state, loggerService, scheduler, gui);

        InitPortHandlers(ioPortDispatcher);
        FlushBuffer();
    }

    private void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Data, this);
        ioPortDispatcher.AddIOPortHandler(KeyboardPorts.Command, this);
    }

    private void WarnBufferFull() {
        const uint thresoldCycles = 150;

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

    private void WarnReadTestInputs() {
        if (!_readTestInputsWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: Reading test inputs not implemented");
            }
            _readTestInputsWarned = true;
        }
    }

    private void WarnVendorLines() {
        if (!_vendorLinesWarned) {
            if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                _loggerService.Warning("I8042: No vendor-specific commands to manipulate controller lines are emulated");
            }
            _vendorLinesWarned = true;
        }
    }

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
        bool isDataFromAux = _status.IsDataFromAux;
        bool isDataFromKbd = !isDataFromAux && _status.IsDataNew;

        if (isDataFromAux && _config.MouseIrqEnabled) {
            _dualPic.ProcessInterruptRequest(IrqNumMouse);
        }
        if (isDataFromKbd && _config.KbdIrqEnabled) {
            _dualPic.ProcessInterruptRequest(IrqNumKbdIbmPc);
        }
    }

    private void FlushBuffer() {
        _status.IsDataNew = false;
        _status.IsDataFromAux = false;

        _bufferStartIdx = 0;
        _bufferNumUsed = 0;

        bool shouldNotifyAux = !_shouldSkipDeviceNotify && !IsReadyForAuxFrame();
        bool shouldNotifyKbd = !_shouldSkipDeviceNotify && !IsReadyForKbdFrame();

        _waitingBytesFromAux = 0;
        _waitingBytesFromKbd = 0;

        if (shouldNotifyAux && IsReadyForAuxFrame()) {
            // TODO: Mouse notify-ready when implemented
        }

        if (shouldNotifyKbd && IsReadyForKbdFrame()) {
            KeyboardDevice.NotifyReadyForFrame();
        }
    }

    private void EnforceBufferSpace(int numBytes = 1) {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numBytes, BufferSize);

        if (BufferSize < _bufferNumUsed + numBytes) {
            WarnBufferFull();
            FlushBuffer();
        }
    }

    private void RestartDelayTimer(double timeMs = PortDelayMs) {
        _delayActive = true;
        int token = ++_delayToken;
        _scheduler.ScheduleEvent("i8042-delay-expire", timeMs, () => {
            if (token != _delayToken) {
                return;
            }
            _delayActive = false;
            MaybeTransferBuffer();
        });
    }

    private bool IsDelayExpired() => !_delayActive;

    private void MaybeTransferBuffer() {
        if (_status.IsDataNew || _bufferNumUsed == 0) {
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
            _status.IsDataFromAux = true;
        } else {
            _status.IsDataFromAux = false;
        }
        _status.IsDataNew = true;
        RestartDelayTimer();
        ActivateIrqsIfNeeded();
    }

    private void BufferAdd(byte value, bool isFromAux = false, bool isFromKbd = false, bool skipDelay = false) {
        if ((isFromAux && _config.DisableAuxPort) ||
            (isFromKbd && _config.DisableKbdPort)) {
            // Byte came from a device which is currently disabled
            return;
        }

        if (_bufferNumUsed >= BufferSize) {
            WarnBufferFull();
            FlushBuffer();
            return;
        }

        int idx = (_bufferStartIdx + _bufferNumUsed++) % BufferSize;

        if (isFromKbd && _config.TranslationEnabled) {
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

    private void BufferAddAux(byte value, bool skipDelay = false) => BufferAdd(value, true, false, skipDelay);
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
                WarnControllerMode();
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
                _config.DisableAuxPort = true;
                break;
            case KeyboardCommand.EnablePortAux: // 0xa8
                // Enable aux (mouse) port
                _config.DisableAuxPort = false;
                break;
            case KeyboardCommand.TestPortAux: // 0xa9
                // Port test. Possible results:
                // 0x01: clock line stuck low
                // 0x02: clock line stuck high
                // 0x03: data line stuck low
                // 0x04: data line stuck high
                // Disables the aux (mouse) port
                _config.DisableAuxPort = true; // disable aux
                FlushBuffer();
                BufferAdd((byte)Response.Ok);
                break;
            case KeyboardCommand.TestController: // 0xaa
                // Controller test. Possible results:
                // 0x55: passed; 0xfc: failed
                // Disables aux (mouse) and keyboard ports, enables translation,
                // enables A20 line, marks self-test as passed.
                _a20Gate.IsEnabled = true;
                _config.DisableAuxPort = true;  // disable aux
                _config.DisableKbdPort = true;  // disable kbd
                _config.TranslationEnabled = true; // enable translation
                _config.SelfTestPassed = true;  // mark self-test passed
                FlushBuffer();
                BufferAdd((byte)Response.SelfTestPassed);
                break;
            case KeyboardCommand.TestPortKbd: // 0xab
                // Port test. Possible results:
                // (as with aux port test)
                // Disables the keyboard port
                _config.DisableKbdPort = true; // disable kbd
                FlushBuffer();
                BufferAdd((byte)Response.Ok); // as with TestPortAux
                break;
            case KeyboardCommand.DiagnosticDump: // 0xac
                // Dump the whole controller internal RAM (16 bytes),
                // output port, input port, test input, and status byte
                WarnInternalRamAccess();
                FlushBuffer();
                _isDiagnosticDump = true;
                DiagDumpByte(_config.ToByte());
                for (byte idx = 1; idx <= 16; idx++) {
                    DiagDumpByte(0);
                }
                DiagDumpByte(GetInputPort());
                DiagDumpByte(GetOutputPort());
                WarnReadTestInputs();
                DiagDumpByte(_status.ToByte());
                break;
            case KeyboardCommand.DisablePortKbd: // 0xad
                // Disable keyboard port; any keyboard command
                // reenables the port
                _config.DisableKbdPort = true;
                break;
            case KeyboardCommand.EnablePortKbd: // 0xae
                // Enable the keyboard port
                _config.DisableKbdPort = false;
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
                WarnReadTestInputs();
                FlushBuffer();
                BufferAdd((byte)Response.Ok);
                break;
            //
            // Unknown or mostly unsupported commands
            //
            default:
                if (IsCmdMemRead(command)) { // 0x20-0x3f
                    // Read internal RAM - dummy, unimplemented
                    WarnInternalRamAccess();
                    BufferAdd((byte)Response.Ok);
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
                if ((param & (byte)OutputPortBits.ResetNotAsserted) == 0) {
                    if (_loggerService.IsEnabled(LogEventLevel.Warning)) {
                        _loggerService.Warning("I8042: Clearing P2 bit 0 locks a real PC");
                    }
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
                BufferAddAux(param);
                break;
            case KeyboardCommand.WriteAux: // 0xd4
                RestartDelayTimer(PortDelayMs * 2);
                _status.Timeout = false;
                // TODO: mouse port write and timeout flag when integrated
                break;
            default:
                if (IsCmdMemWrite(command)) {
                    // ignored
                } else if (IsCmdPulseLine(command)) {
                    byte lines = (byte)(param & (byte)LineParam.AllLines);
                    byte code = (byte)command;
                    if ((code == 0xf0 && param != (byte)LineParam.AllLines && param != (byte)LineParam.AllLinesExceptReset) ||
                        (code != 0xf0 && param != (byte)LineParam.AllLines)) {
                        WarnLinePulse();
                    }
                    if (code == 0xf0 && (lines & (byte)LineParam.Reset) == 0) {
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
        BufferAdd((byte)Response.SpaceScanCodeSet1);
    }

    // ***************************************************************************
    // I/O port handlers
    // ***************************************************************************

    public override byte ReadByte(ushort port) {
        switch (port) {
            case KeyboardPorts.Data: // port 0x60
                if (!_status.IsDataNew) {
                    // Byte already read - just return the previous one
                    return _dataByte;
                }

                if (_isDiagnosticDump && _bufferNumUsed == 0) {
                    // Diagnostic dump finished
                    _isDiagnosticDump = false;
                    if (IsReadyForAuxFrame()) {
                        // TODO: mouse notify-ready
                    }
                    if (IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                }

                bool isDataFromAux = _status.IsDataFromAux;
                bool isDataFromKbd = !isDataFromAux;

                if (isDataFromAux) {
                    if (_waitingBytesFromAux > 0) {
                        --_waitingBytesFromAux;
                    }
                    if (IsReadyForAuxFrame()) {
                        // TODO: mouse notify-ready
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

                _status.IsDataNew = false;     // mark byte as already read
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
                _status.WasLastWriteCmd = false; // was_last_write_cmd = false

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
                        // TODO: mouse notify-ready
                    }
                    if (shouldNotifyKbd && IsReadyForKbdFrame()) {
                        KeyboardDevice.NotifyReadyForFrame();
                    }
                } else {
                    // Send this byte to the keyboard
                    _status.Timeout = false; // clear timeout flag
                    _config.DisableKbdPort = false; // auto-enable keyboard port

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

                _status.WasLastWriteCmd = true;

                _currentCommand = KeyboardCommand.None;
                if (value is <= 0x1f or >= 0x40 and <= 0x5f) {
                    // AMI BIOS systems command aliases
                    ExecuteCommand((KeyboardCommand)(value + 0x20));
                } else {
                    ExecuteCommand((KeyboardCommand)value);
                }

                _shouldSkipDeviceNotify = false;

                if (shouldNotifyAux2 && IsReadyForAuxFrame()) {
                    // TODO: mouse notify-ready
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

    /// <summary>
    /// Adds a single byte from auxiliary device (mouse).
    /// </summary>
    /// <param name="value">The byte to add.</param>
    public void AddAuxByte(byte value) {
        if (_config.DisableAuxPort) {
            return; // aux (mouse) port is disabled
        }

        _status.Timeout = false; // clear timeout flag

        EnforceBufferSpace();
        BufferAddAux(value);
    }

    /// <summary>
    /// Adds a frame of bytes from auxiliary device (mouse).
    /// </summary>
    /// <param name="bytes">The bytes to add.</param>
    public void AddAuxFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || _config.DisableAuxPort) {
            return; // empty frame or aux (mouse) port is disabled
        }

        _status.Timeout = false; // clear timeout flag

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
        if (_config.DisableKbdPort) {
            return; // keyboard port is disabled
        }

        _status.Timeout = false; // clear timeout flag

        EnforceBufferSpace();
        BufferAddKbd(value);
    }

    /// <summary>
    /// Adds a frame of bytes from keyboard.
    /// </summary>
    /// <param name="bytes">The bytes to add.</param>
    internal void AddKbdFrame(IReadOnlyList<byte> bytes) {
        if (bytes == null || bytes.Count == 0 || _config.DisableKbdPort) {
            return; // empty frame or keyboard port is disabled
        }

        _status.Timeout = false; // clear timeout flag

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
        return _waitingBytesFromAux == 0 && !_config.DisableAuxPort && !_isDiagnosticDump;
    }

    /// <summary>
    /// Checks if the controller is ready to accept keyboard frames.
    /// </summary>
    /// <returns>True if ready for keyboard frames.</returns>
    internal bool IsReadyForKbdFrame() {
        return _waitingBytesFromKbd == 0 && !_config.DisableKbdPort && !_isDiagnosticDump;
    }

    // Encapsulates the 8042 status register for easier debugging.
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    private sealed class Status {
        public Status(byte initial = 0) => FromByte(initial);

        // bit0: output buffer status (1 = data available)
        public bool IsDataNew { get; set; }

        // bit1: input buffer status (not used here, kept for completeness)
        public bool InputBufferFull { get; set; }

        // bit2: system flag (not used here)
        public bool SystemFlag { get; set; }

        // bit3: last write was command
        public bool WasLastWriteCmd { get; set; }

        // bit4: unused/reserved in this emulation
        public bool Reserved4 { get; set; }

        // bit5: data came from aux (mouse)
        public bool IsDataFromAux { get; set; }

        // bit6: timeout
        public bool Timeout { get; set; }

        // bit7: parity (not used here)
        public bool ParityError { get; set; }

        // Fast pack into a byte.
        public byte ToByte() {
            byte v = 0;
            if (IsDataNew) v |= (byte)StatusBits.OutputBufferFull;
            if (InputBufferFull) v |= (byte)StatusBits.InputBufferFull;
            if (SystemFlag) v |= (byte)StatusBits.SystemFlag;
            if (WasLastWriteCmd) v |= (byte)StatusBits.LastWriteWasCommand;
            if (Reserved4) v |= (byte)StatusBits.Reserved4;
            if (IsDataFromAux) v |= (byte)StatusBits.DataFromAux;
            if (Timeout) v |= (byte)StatusBits.Timeout;
            if (ParityError) v |= (byte)StatusBits.ParityError;
            return v;
        }

        // Fast unpack from a byte.
        public void FromByte(byte value) {
            IsDataNew = (value & (byte)StatusBits.OutputBufferFull) != 0;
            InputBufferFull = (value & (byte)StatusBits.InputBufferFull) != 0;
            SystemFlag = (value & (byte)StatusBits.SystemFlag) != 0;
            WasLastWriteCmd = (value & (byte)StatusBits.LastWriteWasCommand) != 0;
            Reserved4 = (value & (byte)StatusBits.Reserved4) != 0;
            IsDataFromAux = (value & (byte)StatusBits.DataFromAux) != 0;
            Timeout = (value & (byte)StatusBits.Timeout) != 0;
            ParityError = (value & (byte)StatusBits.ParityError) != 0;
        }

        private string DebuggerDisplay =>
            $"0x{ToByte():X2} (New={IsDataNew}, Aux={IsDataFromAux}, Cmd={WasLastWriteCmd}, TO={Timeout})";
    }

    /// <summary>
    /// Byte 0x00 of the controller memory - configuration byte
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public sealed class ConfigByte {
        public ConfigByte(byte initial = 0) => FromByte(initial);

        // bit0: keyboard IRQ enabled (IRQ1)
        public bool KbdIrqEnabled { get; set; }
        // bit1: mouse IRQ enabled (IRQ12)
        public bool MouseIrqEnabled { get; set; }
        // bit2: self-test passed
        public bool SelfTestPassed { get; set; }
        // bit3: reserved
        public bool Reserved3 { get; set; }
        // bit4: keyboard port disabled
        public bool DisableKbdPort { get; set; }
        // bit5: aux (mouse) port disabled
        public bool DisableAuxPort { get; set; }
        // bit6: translation enabled (AT -> XT)
        public bool TranslationEnabled { get; set; }
        // bit7: reserved
        public bool Reserved7 { get; set; }

        public byte ToByte() {
            byte v = 0;
            if (KbdIrqEnabled) v |= (byte)ConfigBits.KbdIrqEnabled;
            if (MouseIrqEnabled) v |= (byte)ConfigBits.MouseIrqEnabled;
            if (SelfTestPassed) v |= (byte)ConfigBits.SelfTestPassed;
            if (Reserved3) v |= (byte)ConfigBits.Reserved3;
            if (DisableKbdPort) v |= (byte)ConfigBits.DisableKbdPort;
            if (DisableAuxPort) v |= (byte)ConfigBits.DisableAuxPort;
            if (TranslationEnabled) v |= (byte)ConfigBits.TranslationEnabled;
            if (Reserved7) v |= (byte)ConfigBits.Reserved7;
            return v;
        }

        public void FromByte(byte value) {
            KbdIrqEnabled = (value & (byte)ConfigBits.KbdIrqEnabled) != 0;
            MouseIrqEnabled = (value & (byte)ConfigBits.MouseIrqEnabled) != 0;
            SelfTestPassed = (value & (byte)ConfigBits.SelfTestPassed) != 0;
            Reserved3 = (value & (byte)ConfigBits.Reserved3) != 0;
            DisableKbdPort = (value & (byte)ConfigBits.DisableKbdPort) != 0;
            DisableAuxPort = (value & (byte)ConfigBits.DisableAuxPort) != 0;
            TranslationEnabled = (value & (byte)ConfigBits.TranslationEnabled) != 0;
            Reserved7 = (value & (byte)ConfigBits.Reserved7) != 0;
        }

        private string DebuggerDisplay =>
            $"0x{ToByte():X2} (IRQ1={KbdIrqEnabled}, IRQ12={MouseIrqEnabled}, KbdDis={DisableKbdPort}, AuxDis={DisableAuxPort}, Xlate={TranslationEnabled}, SelfTest={SelfTestPassed})";
    }
}