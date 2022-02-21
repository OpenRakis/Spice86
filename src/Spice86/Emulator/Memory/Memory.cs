namespace Spice86.Emulator.Memory;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM.Breakpoint;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    private readonly byte[] _physicalMemory;

    private readonly BreakPointHolder _readBreakPoints = new();

    private readonly BreakPointHolder _writeBreakPoints = new();

    public Memory(uint size) {
        this._physicalMemory = new byte[size];
    }

    public void DumpToFile(string path) {
        File.WriteAllBytes(path, _physicalMemory);
    }

    public byte[] GetData(uint address, int length) {
        byte[] res = new byte[length];
        Array.Copy(_physicalMemory, address, res, 0, length);
        return res;
    }

    public byte[] Ram => _physicalMemory;

    public int Size => _physicalMemory.Length;

    public byte this[uint i] {
        get { return GetUint8(i); }
        set { SetUint8(i, value); }
    }

    public ushort this[ushort i] {
        get { return GetUint16(i); }
        set { SetUint16(i, value); }
    }

    public uint this[int i] {
        get { return GetUint32((uint)i); }
        set { SetUint32((uint)i, value); }
    }


    public ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(_physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public uint GetUint32(uint address) {
        var res = MemoryUtils.GetUint32(_physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public byte GetUint8(uint addr) {
        var res = MemoryUtils.GetUint8(_physicalMemory, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    public void LoadData(uint address, byte[] data, int length) {
        MonitorRangeWriteAccess(address, (uint)(address + length));
        Array.Copy(data, 0, _physicalMemory, address, length);
    }

    public void MemCopy(uint sourceAddress, uint destinationAddress, int length) {
        Array.Copy(_physicalMemory, sourceAddress, _physicalMemory, destinationAddress, length);
    }

    public void Memset(uint address, byte value, uint length) {
        Array.Fill(_physicalMemory, value, (int)address, (int)length);
    }

    public uint? SearchValue(uint address, int len, IList<byte> value) {
        int end = (int)(address + len);
        if (end >= _physicalMemory.Length) {
            end = _physicalMemory.Length;
        }

        for (long i = address; i < end; i++) {
            long endValue = value.Count;
            if (endValue + i >= _physicalMemory.Length) {
                endValue = _physicalMemory.Length - i;
            }

            int j = 0;
            while (j < endValue && _physicalMemory[i + j] == value[j]) {
                j++;
            }

            if (j == endValue) {
                return (uint)i;
            }
        }

        return null;
    }

    public void SetUint16(uint address, ushort value) {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint16(_physicalMemory, address, value);
    }

    public void SetUint32(uint address, uint value) {
        MonitorWriteAccess(address);

        // For convenience, no get as 16 bit apps are not supposed call this directly
        MemoryUtils.SetUint32(_physicalMemory, address, value);
    }

    public void SetUint8(uint address, byte value) {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint8(_physicalMemory, address, value);
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

    private void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(uint address) {
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
    }
}