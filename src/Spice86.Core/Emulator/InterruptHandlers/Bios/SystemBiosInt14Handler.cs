using Microsoft.Extensions.Logging;
namespace Spice86.Core.Emulator.InterruptHandlers.Bios;


using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
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
        ILogger loggerService)
        : base(memory, functionHandlerProvider, stack, state, loggerService) {
        _biosDataArea = biosDataArea;
        _ioPortDispatcher = ioPortDispatcher;
        InitializeBiosDataArea();
    }

    public override byte VectorNumber => 0x14;

    public override void Run() {
        byte operation = State.AH;
        if (LoggerService.IsEnabled(LogLevel.Trace)) {
            LoggerService.LogTrace("BIOS INT14H: AH=0x{Function:X2} DX=0x{PortIndex:X4} AL=0x{Argument:X2}",
                operation, State.DX, State.AL);
        }

        if (!HasRunnable(operation)) {
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("BIOS INT14H: Unsupported function AH=0x{Function:X2}", operation);
            }
            SetCarryFlag(true, true);
            return;
        }

        Run(operation);
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

    /// <summary>Initializes serial port <paramref name="portIndex"/> with
    /// <paramref name="configuration"/>; returns both line/modem status bytes in AX, or
    /// <see langword="null"/> when the port is invalid or absent.</summary>
    public (ushort? ax, bool carryFlag) InitializePort(byte configuration, ushort portIndex) {
        if (!TryGetPort(portIndex, out ushort port)) {
            return (null, false);
        }

        ushort baudDivisor = ComputeBaudDivisor(configuration);
        _ioPortDispatcher.WriteByte((ushort)(port + 3), LcrDivisorAccessBit);
        _ioPortDispatcher.WriteByte(port, (byte)(baudDivisor & 0xFF));
        _ioPortDispatcher.WriteByte((ushort)(port + 1), (byte)(baudDivisor >> 8));
        _ioPortDispatcher.WriteByte((ushort)(port + 3), (byte)(configuration & 0x1F));
        _ioPortDispatcher.WriteByte((ushort)(port + 1), 0);

        byte ah = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        byte al = _ioPortDispatcher.ReadByte((ushort)(port + 6));
        return ((ushort)((ah << 8) | al), false);
    }

    [InterruptService(0x00, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.Read, Io = true)]
    private void InitializePort(bool calledFromVm) {
        (ushort? ax, bool carryFlag) = InitializePort(State.AL, State.DX);
        if (ax is { } value) {
            State.AX = value;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Writes <paramref name="character"/> to serial port
    /// <paramref name="portIndex"/> when the transmitter is empty; returns the line status in
    /// AH, or <see langword="null"/> when the port is invalid or absent.</summary>
    public (byte? ah, bool carryFlag) TransmitCharacter(byte character, ushort portIndex) {
        if (!TryGetPort(portIndex, out ushort port)) {
            return (null, false);
        }

        _ioPortDispatcher.WriteByte((ushort)(port + 4), McrDtrAndRts);
        byte lineStatus = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        byte ah = lineStatus;
        if ((lineStatus & LineStatusTxHoldingEmpty) == LineStatusTxHoldingEmpty) {
            _ioPortDispatcher.WriteByte(port, character);
        } else {
            ah = (byte)(lineStatus | 0x80);
        }

        return (ah, false);
    }

    [InterruptService(0x01, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ah | CpuValues.Carry, Memory = ServiceMemoryEffect.Read, Io = true)]
    private void TransmitCharacter(bool calledFromVm) {
        (byte? ah, bool carryFlag) = TransmitCharacter(State.AL, State.DX);
        if (ah is { } value) {
            State.AH = value;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Reads a character from serial port <paramref name="portIndex"/>; with no
    /// data ready, returns the line status in AH and
    /// <paramref name="previousCharacter"/> in AL, or <see langword="null"/> when the port is
    /// invalid or absent.</summary>
    public (ushort? ax, bool carryFlag) ReceiveCharacter(byte previousCharacter, ushort portIndex) {
        if (!TryGetPort(portIndex, out ushort port)) {
            return (null, false);
        }

        _ioPortDispatcher.WriteByte((ushort)(port + 4), McrDtr);
        byte lineStatus = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        if ((lineStatus & LineStatusRxDataReady) == LineStatusRxDataReady) {
            byte ah = (byte)(lineStatus & LineStatusReadMask);
            byte al = _ioPortDispatcher.ReadByte(port);
            return ((ushort)((ah << 8) | al), false);
        }

        return ((ushort)(((lineStatus | 0x80) << 8) | previousCharacter), false);
    }

    [InterruptService(0x02, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.Read, Io = true)]
    private void ReceiveCharacter(bool calledFromVm) {
        (ushort? ax, bool carryFlag) = ReceiveCharacter(State.AL, State.DX);
        if (ax is { } value) {
            State.AX = value;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    /// <summary>Returns the line and modem status bytes of serial port
    /// <paramref name="portIndex"/> in AX, or <see langword="null"/> when the port is invalid
    /// or absent.</summary>
    public (ushort? ax, bool carryFlag) GetPortStatus(ushort portIndex) {
        if (!TryGetPort(portIndex, out ushort port)) {
            return (null, false);
        }

        byte ah = _ioPortDispatcher.ReadByte((ushort)(port + 5));
        byte al = _ioPortDispatcher.ReadByte((ushort)(port + 6));
        return ((ushort)((ah << 8) | al), false);
    }

    [InterruptService(0x03, Reads = CpuValues.Ah | CpuValues.Al | CpuValues.Dx, Writes = CpuValues.Ax | CpuValues.Carry, Memory = ServiceMemoryEffect.Read, Io = true)]
    private void GetPortStatus(bool calledFromVm) {
        (ushort? ax, bool carryFlag) = GetPortStatus(State.DX);
        if (ax is { } value) {
            State.AX = value;
        }
        SetCarryFlag(carryFlag, calledFromVm);
    }

    private bool TryGetPort(ushort portIndex, out ushort port) {
        if (portIndex >= 4) {
            port = 0;
            if (LoggerService.IsEnabled(LogLevel.Warning)) {
                LoggerService.LogWarning("BIOS INT14H: Invalid serial port index DX=0x{PortIndex:X4}", portIndex);
            }
            return false;
        }

        port = _biosDataArea.PortCom[portIndex];
        if (port == 0) {
            if (LoggerService.IsEnabled(LogLevel.Information)) {
                LoggerService.LogInformation("BIOS INT14H: COM{PortNumber} not present", portIndex + 1);
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
