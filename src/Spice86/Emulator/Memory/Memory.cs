namespace Spice86.Emulator.Memory;

using Spice86.Emulator.Errors;
using Spice86.Emulator.VM.Breakpoint;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    private readonly byte[] _physicalMemory;

    private readonly BreakPointHolder _readBreakPoints = new();

    private readonly BreakPointHolder _writeBreakPoints = new();
    private readonly UInt8Indexer _uint8Indexer;
    private readonly UInt16Indexer _uInt16Indexer;
    private readonly UInt32Indexer _uInt32Indexer;

    // For breakpoints to access what is getting written
    public byte CurrentlyWritingByte { get; private set; } = 0;

    public Memory(uint size) {
        _physicalMemory = new byte[size];
        _uint8Indexer = new(this);
        _uInt16Indexer = new(this);
        _uInt32Indexer = new(this);
    }

    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    /// <param name="writeNull">Value indicating whether a null should be written after the string.</param>
    public void SetString(uint segment, uint offset, string value, bool writeNull) {
        byte[]? buffer = ArrayPool<byte>.Shared.Rent(value.Length);
        try {
            uint length = (uint)Encoding.Latin1.GetBytes(value, buffer);
            for (uint i = 0; i < length; i++) {
                this.SetUint8(offset + i, buffer[(int)i]);
            }

            if (writeNull) {
                this.SetUint8(offset + length, 0);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    public void SetString(uint segment, uint offset, string value) => SetString(segment, offset, value, true);

    public void DumpToFile(string path) {
        File.WriteAllBytes(path, _physicalMemory);
    }

    public Span<byte> GetSpan(int address, int length) {
        MonitorRangeReadAccess((uint)address, (uint)(address + length));
        return _physicalMemory.AsSpan(address, length);
    }

    public byte[] GetData(uint address, uint length) {
        MonitorRangeReadAccess(address, address + length);
        byte[] res = new byte[length];
        Array.Copy(_physicalMemory, address, res, 0, length);
        return res;
    }

    public byte[] Ram => _physicalMemory;

    public int Size => _physicalMemory.Length;



    public UInt8Indexer UInt8 => _uint8Indexer;
    public UInt16Indexer UInt16 => _uInt16Indexer;
    public UInt32Indexer UInt32 => _uInt32Indexer;

    public ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(_physicalMemory, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        return res;
    }

    public uint GetUint32(uint address) {
        uint res = MemoryUtils.GetUint32(_physicalMemory, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        MonitorReadAccess(address + 2);
        MonitorReadAccess(address + 3);
        return res;
    }

    public byte GetUint8(uint addr) {
        byte res = MemoryUtils.GetUint8(_physicalMemory, addr);
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

    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        MonitorRangeReadAccess(sourceAddress, sourceAddress + length);
        MonitorRangeWriteAccess(destinationAddress, destinationAddress + length);
        Array.Copy(_physicalMemory, sourceAddress, _physicalMemory, destinationAddress, length);
    }

    public void Memset(uint address, byte value, uint length) {
        MonitorRangeWriteAccess(address, address + length);
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
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        _physicalMemory[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        _physicalMemory[address + 1] = value1;
    }

    public void SetUint32(uint address, uint value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        _physicalMemory[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        _physicalMemory[address + 1] = value1;

        byte value2 = (byte)(value >> 16);
        MonitorWriteAccess(address + 2, value2);
        _physicalMemory[address + 2] = value2;

        byte value3 = (byte)(value >> 24);
        MonitorWriteAccess(address + 3, value3);
        _physicalMemory[address + 3] = value3;
    }

    public void SetUint8(uint address, byte value) {
        MonitorWriteAccess(address, value);
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
    }
}