using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM.Breakpoint;

using System;
using System.Collections.Generic;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    private readonly BreakPointHolder _readBreakPoints = new();

    private readonly BreakPointHolder _writeBreakPoints = new();
    private readonly Machine? _machine;

    // For breakpoints to access what is getting written
    public byte CurrentlyWritingByte { get; private set; } = 0;

    public Memory(uint sizeInKb, Machine? machine = null) {
        Ram = new byte[sizeInKb * 1024];
        UInt8 = new(this);
        UInt16 = new(this);
        UInt32 = new(this);
        _machine = machine;
    }

    public Span<byte> GetSpan(int address, int length) {
        MonitorRangeReadAccess((uint)address, (uint)(address + length));
        return Ram.AsSpan(address, length);
    }

    public byte[] GetData(uint address, uint length) {
        MonitorRangeReadAccess(address, address + length);
        byte[] res = new byte[length];
        Array.Copy(Ram, address, res, 0, length);
        return res;
    }

    public byte[] Ram { get; }

    public int Size => Ram.Length;

    public UInt8Indexer UInt8 { get; }
    public UInt16Indexer UInt16 { get; }
    public UInt32Indexer UInt32 { get; }

    public virtual ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        return res;
    }

    public virtual uint GetUint32(uint address) {
        uint res = MemoryUtils.GetUint32(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        MonitorReadAccess(address + 2);
        MonitorReadAccess(address + 3);
        return res;
    }

    public virtual byte GetUint8(uint addr) {
        byte res = MemoryUtils.GetUint8(Ram, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    public void LoadData(uint address, byte[] data, int length) {
        MonitorRangeWriteAccess(address, (uint)(address + length));
        Array.Copy(data, 0, Ram, address, length);
    }

    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        MonitorRangeReadAccess(sourceAddress, sourceAddress + length);
        MonitorRangeWriteAccess(destinationAddress, destinationAddress + length);
        Array.Copy(Ram, sourceAddress, Ram, destinationAddress, length);
    }

    public void Memset(uint address, byte value, uint length) {
        MonitorRangeWriteAccess(address, address + length);
        Array.Fill(Ram, value, (int)address, (int)length);
    }

    public uint? SearchValue(uint address, int len, IList<byte> value) {
        int end = (int)(address + len);
        if (end >= Ram.Length) {
            end = Ram.Length;
        }

        for (long i = address; i < end; i++) {
            long endValue = value.Count;
            if (endValue + i >= Ram.Length) {
                endValue = Ram.Length - i;
            }

            int j = 0;
            while (j < endValue && Ram[i + j] == value[j]) {
                j++;
            }

            if (j == endValue) {
                return (uint)i;
            }
        }

        return null;
    }

    public virtual void SetUint16(uint address, ushort value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        Ram[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        Ram[address + 1] = value1;
    }

    public virtual void SetUint32(uint address, uint value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        Ram[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        Ram[address + 1] = value1;

        byte value2 = (byte)(value >> 16);
        MonitorWriteAccess(address + 2, value2);
        Ram[address + 2] = value2;

        byte value3 = (byte)(value >> 24);
        MonitorWriteAccess(address + 3, value3);
        Ram[address + 3] = value3;
    }

    public virtual void SetUint8(uint address, byte value) {
        MonitorWriteAccess(address, value);
        MemoryUtils.SetUint8(Ram, address, value);
    }

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType? type = breakPoint.BreakPointType;
        switch (type) {
            case BreakPointType.READ:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.WRITE:
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.ACCESS:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            default:
                throw new UnrecoverableException($"Trying to add unsupported breakpoint of type {type}");
        }
    }

    private void MonitorRangeReadAccess(uint startAddress, uint endAddress) {
        _readBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(uint address, byte value) {
        CurrentlyWritingByte = value;
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
        // This is a hack that copies bytes written to this area to the internal video ram.
        // TODO: Find a better way to map any area of memory to a device or something else.
        if (_machine != null && address is >= 0xA0000 and <= 0xBFFFF) {
            _machine.VgaCard.SetVramByte(address - 0xA0000, value);
        }
    }
}