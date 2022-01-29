namespace Spice86.Emulator.Memory;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM.Breakpoint;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    private readonly byte[] physicalMemory;

    private readonly BreakPointHolder readBreakPoints = new();

    private readonly BreakPointHolder writeBreakPoints = new();

    public Memory(uint size) {
        this.physicalMemory = new byte[size];
    }

    public void DumpToFile(string path) {
        File.WriteAllBytes(path, physicalMemory);
    }

    public byte[] GetData(uint address, int length) {
        byte[] res = new byte[length];
        Array.Copy(physicalMemory, address, res, 0, length);
        return res;
    }

    public byte[] GetRam() {
        return physicalMemory;
    }

    public int GetSize() {
        return physicalMemory.Length;
    }

    public ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public uint GetUint32(uint address) {
        var res = MemoryUtils.GetUint32(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public byte GetUint8(uint addr) {
        var res = MemoryUtils.GetUint8(physicalMemory, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    public void LoadData(uint address, byte[] data, int length) {
        MonitorRangeWriteAccess(address, (uint)(address + length));
        Array.Copy(data, 0, physicalMemory, address, length);
    }

    public void MemCopy(uint sourceAddress, uint destinationAddress, int length) {
        Array.Copy(physicalMemory, sourceAddress, physicalMemory, destinationAddress, length);
    }

    public void Memset(uint address, byte value, uint length) {
        Array.Fill(physicalMemory, (byte)address, (int)(address + length), value);
    }

    public uint? SearchValue(uint address, int len, IList<byte> value) {
        int end = (int)(address + len);
        if (end >= physicalMemory.Length) {
            end = physicalMemory.Length;
        }

        for (long i = address; i < end; i++) {
            long endValue = value.Count;
            if (endValue + i >= physicalMemory.Length) {
                endValue = physicalMemory.Length - i;
            }

            int j = 0;
            while (j < endValue && physicalMemory[i + j] == value[j]) {
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
        MemoryUtils.SetUint16(physicalMemory, address, value);
    }

    public void SetUint32(uint address, uint value) {
        MonitorWriteAccess(address);

        // For convenience, no get as 16 bit apps are not supposed call this directly
        MemoryUtils.SetUint32(physicalMemory, address, value);
    }

    public void SetUint8(uint address, byte value) {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint8(physicalMemory, address, value);
    }

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType? type = breakPoint.GetBreakPointType();
        switch (type) {
            case BreakPointType.READ:
                readBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.WRITE:
                writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.ACCESS:
                readBreakPoints.ToggleBreakPoint(breakPoint, on);
                writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            default:
                throw new UnrecoverableException($"Trying to add unsupported breakpoint of type {type}");
        }
    }

    private void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorReadAccess(uint address) {
        readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(uint address) {
        writeBreakPoints.TriggerMatchingBreakPoints(address);
    }
}