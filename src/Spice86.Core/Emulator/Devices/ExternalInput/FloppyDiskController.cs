namespace Spice86.Core.Emulator.Devices.ExternalInput;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.Storage;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared.Interfaces;

using System;
using System.Collections.Generic;

/// <summary>
/// Emulates the Intel 82077AA Floppy Disk Controller (FDC) connected to IRQ 6.
/// Supports the most common FDC commands used by DOS programs.
/// </summary>
public sealed class FloppyDiskController : DefaultIOPortHandler {
    private const ushort PortDor = 0x3F2;
    private const ushort PortMsr = 0x3F4;
    private const ushort PortData = 0x3F5;

    private const byte MsrMrq = 0x80;
    private const byte MsrDio = 0x40;
    private const byte MsrCb = 0x10;

    private const byte CmdSpecify = 0x03;
    private const byte CmdSenseDriveStatus = 0x04;
    private const byte CmdRecalibrate = 0x07;
    private const byte CmdSenseInterrupt = 0x08;
    private const byte CmdSeek = 0x0F;
    private const byte CmdReadData = 0xE6;
    private const byte CmdWriteData = 0xC5;
    private const byte CmdReadId = 0x4A;
    private const byte CmdFormatTrack = 0x4D;

    private readonly Action<byte> _raiseIrq;
    private readonly IFloppyDriveAccess _floppyAccess;
    private readonly DmaChannel _dmaChannel;

    private readonly List<byte> _commandBuffer = new();
    private readonly Queue<byte> _resultBuffer = new();
    private readonly byte[] _currentCylinder = new byte[4];

    private FdcPhase _phase = FdcPhase.Idle;
    private byte _currentCommand;
    private int _paramCount;
    private byte _lastSt0;
    private byte _lastPcn;
    private byte _dorRegister;

    /// <summary>
    /// Initialises a new <see cref="FloppyDiskController"/>.
    /// </summary>
    /// <param name="state">The CPU registers and flags.</param>
    /// <param name="failOnUnhandledPort">Whether to throw on unhandled ports.</param>
    /// <param name="loggerService">Logger service implementation.</param>
    /// <param name="raiseIrq">Delegate invoked to raise the FDC interrupt (IRQ 6).</param>
    /// <param name="floppyAccess">Low-level floppy drive access for sector reads/writes.</param>
    /// <param name="dmaChannel">DMA channel 2 used for data transfers.</param>
    public FloppyDiskController(
        State state,
        bool failOnUnhandledPort,
        ILoggerService loggerService,
        Action<byte> raiseIrq,
        IFloppyDriveAccess floppyAccess,
        DmaChannel dmaChannel)
        : base(state, failOnUnhandledPort, loggerService) {
        _raiseIrq = raiseIrq;
        _floppyAccess = floppyAccess;
        _dmaChannel = dmaChannel;
    }

    /// <inheritdoc/>
    public override byte ReadByte(ushort port) {
        if (port == PortMsr) {
            return ReadMainStatusRegister();
        }
        if (port == PortData) {
            return ReadDataFifo();
        }
        return base.ReadByte(port);
    }

    /// <inheritdoc/>
    public override void WriteByte(ushort port, byte value) {
        if (port == PortDor) {
            _dorRegister = value;
            return;
        }
        if (port == PortData) {
            WriteDataFifo(value);
            return;
        }
        base.WriteByte(port, value);
    }

    private byte ReadMainStatusRegister() {
        if (_phase == FdcPhase.Result && _resultBuffer.Count > 0) {
            return (byte)(MsrMrq | MsrDio | MsrCb);
        }
        if (_phase == FdcPhase.Idle) {
            return MsrMrq;
        }
        if (_phase == FdcPhase.Command) {
            return MsrMrq | MsrCb;
        }
        return MsrCb;
    }

    private byte ReadDataFifo() {
        if (_resultBuffer.Count > 0) {
            byte value = _resultBuffer.Dequeue();
            if (_resultBuffer.Count == 0) {
                _phase = FdcPhase.Idle;
            }
            return value;
        }
        return 0xFF;
    }

    private void WriteDataFifo(byte value) {
        if (_phase == FdcPhase.Idle) {
            StartCommand(value);
            return;
        }
        if (_phase == FdcPhase.Command) {
            _commandBuffer.Add(value);
            if (_commandBuffer.Count >= _paramCount) {
                ExecuteCommand();
            }
        }
    }

    private void StartCommand(byte commandByte) {
        _currentCommand = commandByte;
        _commandBuffer.Clear();
        _resultBuffer.Clear();
        _paramCount = GetParamCount(commandByte);
        if (_paramCount == 0) {
            _phase = FdcPhase.Execution;
            ExecuteCommand();
        } else {
            _phase = FdcPhase.Command;
        }
    }

    private static int GetParamCount(byte command) {
        switch (command) {
            case CmdSpecify:
                return 2;
            case CmdSenseDriveStatus:
                return 1;
            case CmdRecalibrate:
                return 1;
            case CmdSenseInterrupt:
                return 0;
            case CmdSeek:
                return 2;
            case CmdReadData:
                return 8;
            case CmdWriteData:
                return 8;
            case CmdReadId:
                return 1;
            case CmdFormatTrack:
                return 5;
            default:
                return 0;
        }
    }

    private void ExecuteCommand() {
        _phase = FdcPhase.Execution;
        switch (_currentCommand) {
            case CmdSpecify:
                ExecuteSpecify();
                break;
            case CmdSenseDriveStatus:
                ExecuteSenseDriveStatus();
                break;
            case CmdRecalibrate:
                ExecuteRecalibrate();
                break;
            case CmdSenseInterrupt:
                ExecuteSenseInterrupt();
                break;
            case CmdSeek:
                ExecuteSeek();
                break;
            case CmdReadData:
                ExecuteReadData();
                break;
            case CmdWriteData:
                ExecuteWriteData();
                break;
            case CmdReadId:
                ExecuteReadId();
                break;
            case CmdFormatTrack:
                ExecuteFormatTrack();
                break;
            default:
                PushResultByte(0x80);
                SetResultPhase();
                break;
        }
    }

    private void ExecuteSpecify() {
        // Accept step rate/head unload time and head load time — no-op for emulation.
        _phase = FdcPhase.Idle;
    }

    private void ExecuteSenseDriveStatus() {
        byte driveSelect = _commandBuffer.Count > 0 ? _commandBuffer[0] : (byte)0;
        byte drive = (byte)(driveSelect & 0x03);
        byte st3 = (byte)(driveSelect & 0x07);
        if (_currentCylinder[drive] == 0) {
            st3 |= 0x10; // bit 4: Track 0 signal — asserted only when head is at cylinder 0
        }
        PushResultByte(st3);
        SetResultPhase();
    }

    private void ExecuteRecalibrate() {
        byte driveSelect = _commandBuffer.Count > 0 ? _commandBuffer[0] : (byte)0;
        byte drive = (byte)(driveSelect & 0x03);
        _currentCylinder[drive] = 0;
        _lastSt0 = (byte)(0x20 | drive);
        _lastPcn = 0;
        _phase = FdcPhase.Idle;
        _raiseIrq(6);
    }

    private void ExecuteSenseInterrupt() {
        PushResultByte(_lastSt0);
        PushResultByte(_lastPcn);
        SetResultPhase();
    }

    private void ExecuteSeek() {
        byte driveHead = _commandBuffer.Count > 0 ? _commandBuffer[0] : (byte)0;
        byte cylinder = _commandBuffer.Count > 1 ? _commandBuffer[1] : (byte)0;
        byte drive = (byte)(driveHead & 0x03);
        _currentCylinder[drive] = cylinder;
        _lastSt0 = (byte)(0x20 | drive);
        _lastPcn = cylinder;
        _phase = FdcPhase.Idle;
        _raiseIrq(6);
    }

    private void ExecuteReadData() {
        if (_commandBuffer.Count < 8) {
            PushSt0St1St2NoChs();
            SetResultPhase();
            return;
        }
        byte driveHead = _commandBuffer[0];
        byte cylinder = _commandBuffer[1];
        byte head = _commandBuffer[2];
        byte sector = _commandBuffer[3];
        byte sectorSizeCode = _commandBuffer[4];
        byte lastSector = _commandBuffer[5];
        int bytesPerSector = 128 << sectorSizeCode;
        byte driveNumber = (byte)(driveHead & 0x01);
        int sectorsPerTrack = GetSectorsPerTrack(driveNumber);

        bool success = TransferSectorsViaDma(driveNumber, cylinder, head, sector, lastSector, sectorsPerTrack, bytesPerSector, isRead: true);
        byte st0 = success ? (byte)(driveHead & 0x07) : (byte)(0x40 | (driveHead & 0x07));
        PushResultByte(st0);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(cylinder);
        PushResultByte(head);
        PushResultByte((byte)(sector + (lastSector - sector + 1)));
        PushResultByte(sectorSizeCode);
        SetResultPhase();
        _raiseIrq(6);
    }

    private void ExecuteWriteData() {
        if (_commandBuffer.Count < 8) {
            PushSt0St1St2NoChs();
            SetResultPhase();
            return;
        }
        byte driveHead = _commandBuffer[0];
        byte cylinder = _commandBuffer[1];
        byte head = _commandBuffer[2];
        byte sector = _commandBuffer[3];
        byte sectorSizeCode = _commandBuffer[4];
        byte lastSector = _commandBuffer[5];
        int bytesPerSector = 128 << sectorSizeCode;
        byte driveNumber = (byte)(driveHead & 0x01);
        int sectorsPerTrack = GetSectorsPerTrack(driveNumber);

        bool success = TransferSectorsViaDma(driveNumber, cylinder, head, sector, lastSector, sectorsPerTrack, bytesPerSector, isRead: false);
        byte st0 = success ? (byte)(driveHead & 0x07) : (byte)(0x40 | (driveHead & 0x07));
        PushResultByte(st0);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(cylinder);
        PushResultByte(head);
        PushResultByte((byte)(sector + (lastSector - sector + 1)));
        PushResultByte(sectorSizeCode);
        SetResultPhase();
        _raiseIrq(6);
    }

    private void ExecuteReadId() {
        byte driveHead = _commandBuffer.Count > 0 ? _commandBuffer[0] : (byte)0;
        byte drive = (byte)(driveHead & 0x03);
        byte cylinder = _currentCylinder[drive];
        PushResultByte((byte)(driveHead & 0x07));
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(cylinder);
        PushResultByte(0x00);
        PushResultByte(0x01);
        PushResultByte(0x02);
        SetResultPhase();
        _raiseIrq(6);
    }

    private void ExecuteFormatTrack() {
        byte driveHead = _commandBuffer.Count > 0 ? _commandBuffer[0] : (byte)0;
        byte sectorSizeCode = _commandBuffer.Count > 1 ? _commandBuffer[1] : (byte)2;
        PushResultByte((byte)(driveHead & 0x07));
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(_currentCylinder[driveHead & 0x03]);
        PushResultByte(0x00);
        PushResultByte(0x01);
        PushResultByte(sectorSizeCode);
        SetResultPhase();
        _raiseIrq(6);
    }

    private bool TransferSectorsViaDma(byte driveNumber, byte cylinder, byte head, byte startSector, byte lastSector, int sectorsPerTrack, int bytesPerSector, bool isRead) {
        int sectorCount = lastSector - startSector + 1;
        int numberOfHeads = GetNumberOfHeads(driveNumber);
        int lba = cylinder * numberOfHeads * sectorsPerTrack + head * sectorsPerTrack + (startSector - 1);
        int byteOffset = lba * bytesPerSector;
        int byteCount = sectorCount * bytesPerSector;
        byte[] buffer = new byte[byteCount];

        if (isRead) {
            bool ok = _floppyAccess.TryRead(driveNumber, byteOffset, buffer, 0, byteCount);
            if (ok) {
                // Write sector data from disk into DMA-mapped memory.
                _dmaChannel.Write(byteCount, buffer);
            }
            return ok;
        } else {
            // Read data from DMA-mapped memory into the transfer buffer.
            _dmaChannel.Read(byteCount, buffer);
            return _floppyAccess.TryWrite(driveNumber, byteOffset, buffer, 0, byteCount);
        }
    }

    private int GetSectorsPerTrack(byte driveNumber) {
        if (_floppyAccess.TryGetGeometry(driveNumber, out int _, out int _, out int sectorsPerTrack, out int _)) {
            return sectorsPerTrack;
        }
        return DefaultSectorsPerTrack;
    }

    private int GetNumberOfHeads(byte driveNumber) {
        if (_floppyAccess.TryGetGeometry(driveNumber, out int _, out int headsPerCylinder, out int _, out int _)) {
            return headsPerCylinder;
        }
        return DefaultNumberOfHeads;
    }

    private const int DefaultSectorsPerTrack = 18;
    private const int DefaultNumberOfHeads = 2;

    private void PushResultByte(byte value) {
        _resultBuffer.Enqueue(value);
    }

    private void PushSt0St1St2NoChs() {
        PushResultByte(0x40);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(0x00);
        PushResultByte(0x02);
    }

    private void SetResultPhase() {
        _phase = FdcPhase.Result;
    }

    private enum FdcPhase {
        Idle,
        Command,
        Execution,
        Result
    }
}
