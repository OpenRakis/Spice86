namespace Spice86.Core.Emulator.InterruptHandlers.Bios;

using Serilog.Events;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// BIOS INT 14h serial port services.
/// </summary>
public sealed class SystemBiosInt14Handler : InterruptHandler {
    private const ushort DefaultCom1BasePort = 0x03F8;
    private const ushort DefaultCom2BasePort = 0x02F8;
    private const ushort DefaultCom3BasePort = 0x03E8;
    private const ushort DefaultCom4BasePort = 0x02E8;
    private const byte DefaultTimeoutSeconds = 1;
    private const byte LcrDivisorAccessBit = 0x80;
    private const byte McrDtrAndRts = 0x03;
    private const byte McrDtr = 0x01;
    private const byte LineStatusTxHoldingEmpty = 0x20;
    private const byte LineStatusRxDataReady = 0x01;
    private const byte LineStatusReadMask = 0x1E;

    private readonly BiosDataArea _biosDataArea;
    private readonly IOPortDispatcher _ioPortDispatcher;

    public SystemBiosInt14Handler(IMemory memory,
        IFunctionHandlerProvider functionHandlerProvider,
        Stack stack,
        State state,
        BiosDataArea biosDataArea,
        IOPortDispatcher ioPortDispatcher,
        ILoggerService loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _ioPortDispatcher = ioPortDispatcher;
        InitializeBiosDataArea();
        FillDispatchTable();
    }

    public override byte VectorNumber => 0x14;

    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogEventLevel.Verbose)) {
            LoggerService.Verbose("BIOS INT14H: AH=0x{Function:X2} DX=0x{PortIndex:X4} AL=0x{Argument:X2}",
                operation, State.DX, State.AL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("BIOS INT14H: Unsupported function AH=0x{Function:X2}", operation);
            }
            SetCarryFlag(true, true);
            return;
        }

        Run(operation);
    }

    private void FillDispatchTable() {
        AddAction(0x00, () => InitializePort(true));
        AddAction(0x01, () => TransmitCharacter(true));
        AddAction(0x02, () => ReceiveCharacter(true));
        AddAction(0x03, () => GetPortStatus(true));
    }

    private void InitializeBiosDataArea() {
        if (_biosDataArea.PortCom[0] == 0) {
            _biosDataArea.PortCom[0] = DefaultCom1BasePort;
        }
        if (_biosDataArea.PortCom[1] == 0) {
            _biosDataArea.PortCom[1] = DefaultCom2BasePort;
        }
        if (_biosDataArea.PortCom[2] == 0) {
            _biosDataArea.PortCom[2] = DefaultCom3BasePort;
        }
        if (_biosDataArea.PortCom[3] == 0) {
            _biosDataArea.PortCom[3] = DefaultCom4BasePort;
        }

        for (int i = 0; i < 4; i++) {
            if (_biosDataArea.ComTimeout[i] == 0) {
                _biosDataArea.ComTimeout[i] = DefaultTimeoutSeconds;
            }
        }
    }

    private void InitializePort(bool calledFromVm) {
        if (!TryGetPort(State.DX, out ushort port)) {
            SetCarryFlag(false, calledFromVm);
            return;
        }

        ushort baudDivisor = ComputeBaudDivisor(State.AL);
        _ioPortDispatcher.WriteByte((ushort)(port + 3), LcrDivisorAccessBit);
        _ioPortDispatcher.WriteByte(port, (byte)(baudDivisor & 0xFF));
        _ioPortDispatcher.WriteByte((ushort)(port + 1), (byte)(baudDivisor >> 8));
        _ioPortDispatcher.WriteByte((ushort)(port + 3), (byte)(State.AL & 0x1F));
        _ioPortDispatcher.WriteByte((ushort)(port + 1), 0);

        State.AH = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        State.AL = _ioPortDispatcher.ReadByte((ushort)(port + 6));
        SetCarryFlag(false, calledFromVm);
    }

    private void TransmitCharacter(bool calledFromVm) {
        if (!TryGetPort(State.DX, out ushort port)) {
            SetCarryFlag(false, calledFromVm);
            return;
        }

        _ioPortDispatcher.WriteByte((ushort)(port + 4), McrDtrAndRts);
        byte lineStatus = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        State.AH = lineStatus;
        if ((lineStatus & LineStatusTxHoldingEmpty) == LineStatusTxHoldingEmpty) {
            _ioPortDispatcher.WriteByte(port, State.AL);
        } else {
            State.AH = (byte)(lineStatus | 0x80);
        }

        SetCarryFlag(false, calledFromVm);
    }

    private void ReceiveCharacter(bool calledFromVm) {
        if (!TryGetPort(State.DX, out ushort port)) {
            SetCarryFlag(false, calledFromVm);
            return;
        }

        _ioPortDispatcher.WriteByte((ushort)(port + 4), McrDtr);
        byte lineStatus = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        if ((lineStatus & LineStatusRxDataReady) == LineStatusRxDataReady) {
            State.AH = (byte)(lineStatus & LineStatusReadMask);
            State.AL = _ioPortDispatcher.ReadByte(port);
        } else {
            State.AH = (byte)(lineStatus | 0x80);
        }

        SetCarryFlag(false, calledFromVm);
    }

    private void GetPortStatus(bool calledFromVm) {
        if (!TryGetPort(State.DX, out ushort port)) {
            SetCarryFlag(false, calledFromVm);
            return;
        }

        State.AH = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        State.AL = _ioPortDispatcher.ReadByte((ushort)(port + 6));
        SetCarryFlag(false, calledFromVm);
    }

    private bool TryGetPort(ushort portIndex, out ushort port) {
        if (portIndex >= 4) {
            port = 0;
            if (LoggerService.IsEnabled(LogEventLevel.Warning)) {
                LoggerService.Warning("BIOS INT14H: Invalid serial port index DX=0x{PortIndex:X4}", portIndex);
            }
            return false;
        }

        port = _biosDataArea.PortCom[portIndex];
        if (port == 0) {
            if (LoggerService.IsEnabled(LogEventLevel.Information)) {
                LoggerService.Information("BIOS INT14H: COM{PortNumber} not present", portIndex + 1);
            }
            return false;
        }

        return true;
    }

    private static ushort ComputeBaudDivisor(byte parameter) {
        return (byte)(parameter >> 5) switch {
            0 => (ushort)1047,
            1 => (ushort)768,
            2 => (ushort)384,
            3 => (ushort)192,
            4 => (ushort)96,
            5 => (ushort)48,
            6 => (ushort)24,
            _ => (ushort)12,
        };
    }
}