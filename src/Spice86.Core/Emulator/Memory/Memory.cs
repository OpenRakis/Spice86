namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    private readonly uint _addressMask = 0x000FFFFFu;

    /// <summary>
    /// Starting physical address of video RAM.
    /// </summary>
    private const int VramAddress = 0xA000 << 4;

    /// <summary>
    /// The highest address which is mapped in the class VgaCard
    /// </summary>
    /// <remarks>
    /// Video RAM mapping is technically up to 0xBFFF0 normally.
    /// </remarks>
    private const int VramUpperBound = 0xBFFF << 4;

    /// <summary>
    /// Size of conventional memory in bytes.
    /// </summary>
    public const uint ConvMemorySize = 1024 * 1024;

    public bool EnableA20 { get; internal set; }

    public int MemorySize { get; init; }

    private readonly BreakPointHolder _readBreakPoints = new();

    private readonly BreakPointHolder _writeBreakPoints = new();

    private readonly MetaAllocator _metaAllocator = new();

    // For breakpoints to access what is getting written
    public byte CurrentlyWritingByte { get; private set; } = 0;

    /// <summary>
    /// Gets or sets the EMS handler.
    /// </summary>
    public ExpandedMemoryManager? Ems { get; internal set; }

    private readonly Machine _machine;

    public Memory(Machine machine, uint sizeInKb) {
        if (sizeInKb * 1024 < ConvMemorySize) {
            throw new ArgumentException("Memory size must be at least 1 MB.");
        }
        this.MemorySize = (int)sizeInKb * 1024;

        // Reserve room for the real-mode interrupt table.
        this.Reserve(0x0000, 256 * 4);

        // Reserve VGA video RAM window.
        this.Reserve(0xA000, VramUpperBound - VramAddress + 16u);

        _machine = machine;
        Ram = new byte[sizeInKb * 1024];
        unsafe {
            fixed(byte* ramPtr = this.Ram) {
                RawView = ramPtr;
            }
        }
        UInt8 = new(this);
        UInt16 = new(this);
        UInt32 = new(this);
    }

    /// <summary>
    /// Reserves a block of conventional memory.
    /// </summary>
    /// <param name="minimumSegment">Minimum segment of requested memory block.</param>
    /// <param name="length">Size of memory block in bytes.</param>
    /// <returns>Information about the reserved block of memory.</returns>
    public ReservedBlock Reserve(ushort minimumSegment, uint length) {
        ushort allocation = _metaAllocator.Allocate(minimumSegment, (int)length);
        return new(allocation, length);
    }

    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    public void SetString(uint segment, uint offset, string value) => SetString(segment, offset, value, true);

    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    /// <param name="writeNull">Value indicating whether a null should be written after the string.</param>
    public void SetString(uint segment, uint offset, string value, bool writeNull)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(value.Length);
        try
        {
            uint length = (uint)Encoding.Latin1.GetBytes(value, buffer);
            for (uint i = 0; i < length; i++) {
                this.SetByte(segment, offset + i, buffer[(int)i]);
            }

            if (writeNull) {
                this.SetByte(segment, offset + length, 0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads an ANSI string from emulated memory with a specified length.
    /// </summary>
    /// <param name="segment">Segment of string to read.</param>
    /// <param name="offset">Offset of string to read.</param>
    /// <param name="length">Length of the string in bytes.</param>
    /// <returns>String read from the specified segment and offset.</returns>
    public string GetString(uint segment, uint offset, int length)
    {
        IntPtr ptr = GetPointer(segment, offset);
        return Marshal.PtrToStringAnsi(ptr, length);
    }

    /// <summary>
    /// Reads an ANSI string from emulated memory with a maximum length and end sentinel character.
    /// </summary>
    /// <param name="segment">Segment of string to read.</param>
    /// <param name="offset">Offset of string to read.</param>
    /// <param name="maxLength">Maximum number of bytes to read.</param>
    /// <param name="sentinel">End sentinel character of the string to read.</param>
    /// <returns>String read from the specified segment and offset.</returns>
    public string GetString(uint segment, uint offset, int maxLength, byte sentinel)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLength);
        try
        {
            uint i;

            for (i = 0; i < maxLength; i++)
            {
                byte value = this.GetByte(segment, offset + i);
                if (value == sentinel) {
                    break;
                }

                buffer[i] = value;
            }

            return Encoding.Latin1.GetString(buffer.AsSpan(0, (int)i));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Pointer to the start of the emulated physical memory.
    /// </summary>
    private unsafe byte* RawView { get; set; }

    /// <summary>
    /// Gets a pointer to a location in the emulated memory.
    /// </summary>
    /// <param name="segment">Segment of pointer.</param>
    /// <param name="offset">Offset of pointer.</param>
    /// <returns>Pointer to the emulated location at segment:offset.</returns>
    public IntPtr GetPointer(uint segment, uint offset)
    {
        unsafe
        {
            return new IntPtr(RawView + GetRealModePhysicalAddress(segment, offset));
        }
    }

    private uint GetRealModePhysicalAddress(uint segment, uint offset) => ((segment << 4) + offset) & _addressMask;

    /// <summary>
    /// Gets a pointer to a location in the emulated memory.
    /// </summary>
    /// <param name="address">Address of pointer.</param>
    /// <returns>Pointer to the specified address.</returns>
    public IntPtr GetPointer(int address)
    {
        address &= (int)_addressMask;

        unsafe
        {
            return new IntPtr(RawView + address);
        }
    }

    /// <summary>
    /// Gets the entire emulated RAM as a Span
    /// </summary>
    public Span<byte> Span {
        get {
            unsafe {
                return new Span<byte>(this.RawView, this.MemorySize);
            }
        }
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

    /// <summary>
    /// Reads a byte from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of byte to read.</param>
    /// <param name="offset">Offset of byte to read.</param>
    /// <returns>Byte at the specified segment and offset.</returns>
    public byte GetByte(uint segment, uint offset) => this.RealModeRead<byte>(segment, offset);

    /// <summary>
    /// Reads a byte from emulated memory.
    /// </summary>
    /// <param name="address">Physical address of byte to read.</param>
    /// <returns>Byte at the specified address.</returns>
    public byte GetByte(uint address)
    {
        return this.PhysicalRead<byte>(address);
    }

    private T PhysicalRead<T>(uint address, bool mask = true) where T : unmanaged
    {
        uint fullAddress = mask ? (address & this._addressMask) : address;

        unsafe
        {
            if (this.Ems != null && fullAddress is >= (ExpandedMemoryManager.PageFrameSegment << 4) and < (ExpandedMemoryManager.PageFrameSegment << 4) + 65536)
            {
                return Unsafe.ReadUnaligned<T>(this.RawView + this.Ems.GetMappedAddress(address));
            }
            else
            {
                return Unsafe.ReadUnaligned<T>(this.RawView + fullAddress);
            }
        }
    }

    private void PhysicalWrite<T>(uint address, T value, bool mask = true) where T : unmanaged
    {
        uint fullAddress = mask ? (address & this._addressMask) : address;

        unsafe
        {
            if (this.Ems != null && fullAddress is >= (ExpandedMemoryManager.PageFrameSegment << 4) and < (ExpandedMemoryManager.PageFrameSegment << 4) + 65536)
            {
                Unsafe.WriteUnaligned(this.RawView + this.Ems.GetMappedAddress(address), value);
            }
            else
            {
                Unsafe.WriteUnaligned(this.RawView + fullAddress, value);
            }
        }
    }

    private T RealModeRead<T>(uint segment, uint offset) where T : unmanaged => PhysicalRead<T>(GetRealModePhysicalAddress(segment, offset));

    private void RealModeWrite<T>(uint segment, uint offset, T value) where T : unmanaged => this.PhysicalWrite(this.GetRealModePhysicalAddress(segment, offset), value);

    /// <summary>
    /// Writes a byte to emulated memory.
    /// </summary>
    /// <param name="segment">Segment of byte to write.</param>
    /// <param name="offset">Offset of byte to write.</param>
    /// <param name="value">Value to write to the specified segment and offset.</param>
    public void SetByte(uint segment, uint offset, byte value) => this.RealModeWrite(segment, offset, value);

    /// <summary>
    /// Writes a byte to emulated memory.
    /// </summary>
    /// <param name="address">Physical address of byte to write.</param>
    /// <param name="value">Value to write to the specified address.</param>
    public void SetByte(uint address, byte value)
    {
        this.PhysicalWrite(address, value);
    }

    public byte[] Ram { get; }

    public int Size => Ram.Length;

    public UInt8Indexer UInt8 { get; }
    public UInt16Indexer UInt16 { get; }
    public UInt32Indexer UInt32 { get; }

    public ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        return res;
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 16-bit integer to read.</param>
    /// <param name="offset">Offset of unsigned 16-bit integer to read.</param>
    /// <returns>Unsigned 16-bit integer at the specified segment and offset.</returns>
    public ushort GetUint16(uint segment, uint offset) => this.RealModeRead<ushort>(segment, offset);

    /// <summary>
    /// Reads an unsigned 32-bit integer from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 32-bit integer to read.</param>
    /// <param name="offset">Offset of unsigned 32-bit integer to read.</param>
    /// <returns>Unsigned 32-bit integer at the specified segment and offset.</returns>
    public uint GetUint32(uint segment, uint offset) => this.RealModeRead<uint>(segment, offset);

    public uint GetUint32(uint address) {
        uint res = MemoryUtils.GetUint32(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        MonitorReadAccess(address + 2);
        MonitorReadAccess(address + 3);
        return res;
    }

    public byte GetUint8(uint addr) {
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

    public void SetUint16(uint address, ushort value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        Ram[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        Ram[address + 1] = value1;
    }

    public void SetUint32(uint address, uint value) {
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

    public void SetUint8(uint address, byte value) {
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
    }
}