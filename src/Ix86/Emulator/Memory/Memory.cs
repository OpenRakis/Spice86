namespace Ix86.Emulator.Memory;

using Ix86.Utils;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Addressable memory of the machine.
/// </summary>
public class Memory
{
    private byte[] physicalMemory;
    // TODO
    //private BreakPointHolder readBreakPoints = new BreakPointHolder();
    //private BreakPointHolder writeBreakPoints = new BreakPointHolder();
    public Memory(int size)
    {
        this.physicalMemory = new byte[size];
    }

    public virtual int GetSize()
    {
        return physicalMemory.Length;
    }

    public virtual byte[] GetRam()
    {
        return physicalMemory;
    }

    public virtual void LoadData(int address, byte[] data)
    {
        LoadData(address, data, data.Length);
    }

    public virtual void LoadData(int address, byte[] data, int length)
    {
        MonitorRangeWriteAccess(address, address + length);
        Array.Copy(data, 0, physicalMemory, address, length);
    }

    public virtual byte[] GetData(int address, int length)
    {
        byte[] res = new byte[length];
        Array.Copy(physicalMemory, address, res, 0, length);
        return res;
    }

    public virtual void MemCopy(int sourceAddress, int destinationAddress, int length)
    {
        Array.Copy(physicalMemory, sourceAddress, physicalMemory, destinationAddress, length);
    }

    public virtual void Memset(int address, int value, int length)
    {
        Array.Fill(physicalMemory, (byte)address, address + length, ConvertUtils.Uint8b(value));
    }

    public virtual int GetUint8(int addr)
    {
        int res = MemoryUtils.GetUint8(physicalMemory, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public virtual void SetUint8(int address, int value)
    {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint8(physicalMemory, address, value);
    }

    public virtual int GetUint16(int address)
    {
        int res = MemoryUtils.GetUint16(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public virtual void SetUint16(int address, int value)
    {
        MonitorWriteAccess(address);
        MemoryUtils.SetUint16(physicalMemory, address, value);
    }

    public virtual int GetUint32(int address)
    {
        int res = MemoryUtils.GetUint32(physicalMemory, address);
        MonitorReadAccess(address);
        return res;
    }

    public virtual void SetUint32(int address, int value)
    {
        MonitorWriteAccess(address);

        // For convenience, no get as 16 bit apps are not supposed call this directly
        MemoryUtils.SetUint32(physicalMemory, address, value);
    }

    public virtual int? SearchValue(int address, int len, IList<Byte> value)
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

    public virtual void DumpToFile(string path)
    {
        File.WriteAllBytes(path, physicalMemory);
    }

    private void MonitorReadAccess(int address)
    {
        //readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(int address)
    {
        //writeBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorRangeWriteAccess(int startAddress, int endAddress)
    {
        //writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }
}
