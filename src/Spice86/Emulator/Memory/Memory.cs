namespace Spice86.Emulator.Memory;

using Spice86.Emulator.Errors;
using Spice86.Emulator.Machine.Breakpoint;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary> Addressable memory of the machine. </summary>
public class Memory
{
    private readonly byte[] physicalMemory;

    private readonly BreakPointHolder readBreakPoints = new();

    private readonly BreakPointHolder writeBreakPoints = new();

    public Memory(int size)
    {
        this.physicalMemory = new byte[size];
    }

    public void DumpToFile(string path)
    {
        File.WriteAllBytes(path, physicalMemory);
    }

    public byte[] GetData(int address, int length)
    {
        byte[] res = new byte[length];
        Array.Copy(physicalMemory, address, res, 0, length);
        return res;
    }

    public byte[] GetRam()
    {
        return physicalMemory;
    }

    public int GetSize()
    {
        return physicalMemory.Length;
    }

    public int GetUint16(int address)
    {
        int res = MemoryUtils.GetUint16(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public int GetUint32(int address)
    {
        int res = MemoryUtils.GetUint32(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public int GetUint8(int addr)
    {
        int res = MemoryUtils.GetUint8(physicalMemory, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public void LoadData(int address, byte[] data)
    {
        LoadData(address, data, data.Length);
    }

    public void LoadData(int address, byte[] data, int length)
    {
        MonitorRangeWriteAccess(address, address + length);
        Array.Copy(data, 0, physicalMemory, address, length);
    }

    public void MemCopy(int sourceAddress, int destinationAddress, int length)
    {
        Array.Copy(physicalMemory, sourceAddress, physicalMemory, destinationAddress, length);
    }

    public void Memset(int address, int value, int length)
    {
        Array.Fill(physicalMemory, (byte)address, address + length, ConvertUtils.Uint8b(value));
    }

    public int? SearchValue(int address, int len, IList<Byte> value)
    {
        int end = address + len;
        if (end >= physicalMemory.Length)
        {
            end = physicalMemory.Length;
        }

        for (int i = address; i < end; i++)
        {
            int endValue = value.Count;
            if (endValue + i >= physicalMemory.Length)
            {
                endValue = physicalMemory.Length - i;
            }

            int j = 0;
            while (j < endValue && physicalMemory[i + j] == value[j])
            {
                j++;
            }

            if (j == endValue)
            {
                return i;
            }
        }

        return null;
    }

    public void SetUint16(int address, int value)
    {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint16(physicalMemory, address, value);
    }

    public void SetUint32(int address, int value)
    {
        MonitorWriteAccess(address);

        // For convenience, no get as 16 bit apps are not supposed call this directly
        MemoryUtils.SetUint32(physicalMemory, address, value);
    }

    public void SetUint8(int address, int value)
    {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint8(physicalMemory, address, value);
    }

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on)
    {
        var type = breakPoint.GetBreakPointType();
        if (type == BreakPointType.READ)
        {
            readBreakPoints.ToggleBreakPoint(breakPoint, on);
        }
        if (type == BreakPointType.WRITE)
        {
            writeBreakPoints.ToggleBreakPoint(breakPoint, on);
        }
        if (type == BreakPointType.ACCESS)
        {
            readBreakPoints.ToggleBreakPoint(breakPoint, on);
            writeBreakPoints.ToggleBreakPoint(breakPoint, on);
        }
        throw new UnrecoverableException($"Trying to add unsupported breakpoint of type {type}");
    }

    private void MonitorRangeWriteAccess(int startAddress, int endAddress)
    {
        writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorReadAccess(int address)
    {
        readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(int address)
    {
        writeBreakPoints.TriggerMatchingBreakPoints(address);
    }
}