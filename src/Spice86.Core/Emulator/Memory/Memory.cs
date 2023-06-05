namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;

using System.Text;

/// <summary>
///     Represents the memory bus of the IBM PC.
/// </summary>
public class Memory {
    private readonly IMemoryDevice _ram;
    private readonly BreakPointHolder _readBreakPoints = new();
    private readonly BreakPointHolder _writeBreakPoints = new();
    private IMemoryDevice[] _memoryDevices;
    private readonly List<DeviceRegistration> _devices = new();

    /// <summary>
    /// Instantiate a new memory bus.
    /// </summary>
    /// <param name="baseMemory">The memory device that should provide the default memory implementation</param>
    public Memory(IMemoryDevice baseMemory) {
        uint memorySize = baseMemory.Size;
        _memoryDevices = new IMemoryDevice[memorySize];
        _ram = new Ram(memorySize);
        RegisterMapping(0, memorySize, _ram);
        UInt8 = new UInt8Indexer(this);
        UInt16 = new UInt16Indexer(this);
        UInt32 = new UInt32Indexer(this);
    }

    /// <summary>
    /// The Memory Bus size, in real mode.
    /// </summary>
    public const uint MemoryBusSize = 0x10FFEF;

    /// <summary>
    /// Gets a copy of the current memory state.
    /// </summary>
    public byte[] Ram {
        get {
            byte[] copy = new byte[_memoryDevices.Length];
            for (uint address = 0; address < copy.Length; address++) {
                copy[address] = _memoryDevices[address].Read(address);
            }
            return copy;
        }
    }

    /// <summary>
    ///     Writes a 4-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint32(uint address, uint value) {
        Write(address, (byte)value);
        Write(address + 1, (byte)(value >> 8));
        Write(address + 2, (byte)(value >> 16));
        Write(address + 3, (byte)(value >> 24));
    }

    /// <summary>
    ///     Writes a 2-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint16(uint address, ushort value) {
        Write(address, (byte)value);
        Write(address + 1, (byte)(value >> 8));
    }

    /// <summary>
    ///     Writes a 1-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint8(uint address, byte value) {
        Write(address, value);
    }

    /// <summary>
    ///     Read a 4-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public uint GetUint32(uint address) {
        return (uint)(Read(address) | Read(address + 1) << 8 | Read(address + 2) << 16 | Read(address + 3) << 24);
    }
    
    /// <summary>
    /// Returns a <see cref="Span{T}"/> that represents the specified range of memory.
    /// </summary>
    /// <param name="address">The starting address of the memory range.</param>
    /// <param name="length">The length of the memory range.</param>
    /// <returns>A <see cref="Span{T}"/> instance that represents the specified range of memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no memory device supports the specified memory range.</exception>
    public Span<byte> GetSpan(int address, int length) {
        foreach (DeviceRegistration device in _devices) {
            if (address >= device.StartAddress && address + length <= device.EndAddress) {
                MonitorRangeReadAccess((uint)address, (uint)(address + length));
                return device.Device.GetSpan(address, length);
            }
        }
        throw new InvalidOperationException($"No Memory Device supports a span from {address} to {address + length}");
    }
    
    /// <summary>
    /// Returns an array of bytes read from RAM.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of bytes, read from RAM.</returns>
    public byte[] GetData(uint address, uint length) {
        byte[] data = new byte[length];
        for (uint i = 0; i < length; i++) {
            data[i] = Read(address + i);
        }
        return data;
    }

    /// <summary>
    ///     Read a 2-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public ushort GetUint16(uint address) {
        return (ushort)(Read(address) | Read(address + 1) << 8);
    }

    /// <summary>
    ///     Read a 1-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public byte GetUint8(uint address) {
        return Read(address);
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    /// <param name="length">How many bytes to read from the byte array</param>
    public void LoadData(uint address, byte[] data, int length) {
        for (int i = 0; i < length; i++) {
            Write((uint)(address + i), data[i]);
        }
    }
    /// <summary>
    ///     Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    public void LoadData(uint address, ushort[] data) {
        LoadData(address, data, data.Length);
    }

    /// <summary>
    ///     Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    /// <param name="length">How many words to read from the byte array</param>
    public void LoadData(uint address, ushort[] data, int length) {
        for (int i = 0; i < length; i++) {
            SetUint16((uint)(address + i), data[i]);
        }
    }

    /// <summary>
    ///     Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from</param>
    /// <param name="destinationAddress">The address in memory to start writing to</param>
    /// <param name="length">How many bytes to copy</param>
    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        for (int i = 0; i < length; i++) {
            Write((uint)(destinationAddress + i), Read((uint)(sourceAddress + i)));
        }
    }

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The byte value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset(uint address, byte value, uint amount) {
        for (int i = 0; i < amount; i++) {
            Write((uint)(address + i), value);
        }
    }
    
    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The ushort value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset(uint address, ushort value, uint amount) {
        for (int i = 0; i < amount; i += 2) {
            SetUint16((uint)(address + i), value);
        }
    }

    /// <summary>
    ///     Find the address of a value in memory.
    /// </summary>
    /// <param name="address">The address in memory to start the search from</param>
    /// <param name="len">The maximum amount of memory to search</param>
    /// <param name="value">The sequence of bytes to search for</param>
    /// <returns>The address of the first occurence of the specified sequence of bytes, or null if not found.</returns>
    public uint? SearchValue(uint address, int len, IList<byte> value) {
        int end = (int)(address + len);
        if (end >= _memoryDevices.Length) {
            end = _memoryDevices.Length;
        }

        for (long i = address; i < end; i++) {
            long endValue = value.Count;
            if (endValue + i >= _memoryDevices.Length) {
                endValue = _memoryDevices.Length - i;
            }

            int j = 0;
            while (j < endValue && _memoryDevices[i + j].Read((uint)(i + j)) == value[j]) {
                j++;
            }

            if (j == endValue) {
                return (uint)i;
            }
        }

        return null;
    }

    /// <summary>
    ///     Enable or disable a memory breakpoint.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to enable or disable</param>
    /// <param name="on">true to enable a breakpoint, false to disable it</param>
    /// <exception cref="NotSupportedException"></exception>
    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType type = breakPoint.BreakPointType;
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
            case BreakPointType.EXECUTION:
            case BreakPointType.CYCLES:
            case BreakPointType.MACHINE_STOP:
            default:
                throw new NotSupportedException($"Trying to add unsupported breakpoint of type {type}");
        }
    }


    /// <summary>
    ///     Allows write breakpoints to access the byte being written before it actually is.
    /// </summary>
    public byte CurrentlyWritingByte {
        get;
        set;
    }
    /// <summary>
    ///     The number of bytes in the memory map.
    /// </summary>
    public int Size => _memoryDevices.Length;

    /// <summary>
    ///     Allows indexed byte access to the memory map.
    /// </summary>
    public UInt8Indexer UInt8 {
        get;
    }

    /// <summary>
    ///     Allows indexed word access to the memory map.
    /// </summary>
    public UInt16Indexer UInt16 {
        get;
    }

    /// <summary>
    ///     Allows indexed double word access to the memory map.
    /// </summary>
    public UInt32Indexer UInt32 {
        get;
    }

    /// <summary>
    ///     Allow a class to register for a certain memory range.
    /// </summary>
    /// <param name="baseAddress">The start of the frame</param>
    /// <param name="size">The size of the window</param>
    /// <param name="memoryDevice">The memory device to use</param>
    public void RegisterMapping(uint baseAddress, uint size, IMemoryDevice memoryDevice) {
        uint endAddress = baseAddress + size;
        if (endAddress >= _memoryDevices.Length) {
            Array.Resize(ref _memoryDevices, (int)endAddress);
        }
        for (uint i = baseAddress; i < endAddress; i++) {
            _memoryDevices[i] = memoryDevice;
        }
        _devices.Add(new DeviceRegistration(baseAddress, endAddress, memoryDevice));
    }

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns></returns>
    public string GetZeroTerminatedString(uint address, int maxLength) {
        StringBuilder res = new();
        for (int i = 0; i < maxLength; i++) {
            byte characterByte = GetUint8((uint)(address + i));
            if (characterByte == 0) {
                break;
            }
            char character = Convert.ToChar(characterByte);
            res.Append(character);
        }

        return res.ToString();
    }

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string</param>
    /// <param name="value">The string to write</param>
    /// <param name="maxLength">The maximum length to write</param>
    /// <exception cref="UnrecoverableException"></exception>
    public void SetZeroTerminatedString(uint address, string value, int maxLength) {
        if (value.Length + 1 > maxLength) {
            throw new UnrecoverableException($"String {value} is more than {maxLength} cannot write it at offset {address}");
        }
        int i = 0;
        for (; i < value.Length; i++) {
            char character = value[i];
            byte charFirstByte = Encoding.ASCII.GetBytes(character.ToString())[0];
            SetUint8((uint)(address + i), charFirstByte);
        }

        SetUint8((uint)(address + i), 0);
    }
    
        
    public string GetString(uint address, int length) {
        StringBuilder res = new();
        for (int i = 0; i < length; i++) {
            char character = (char)Read((uint)(address + i));
            res.Append(character);
        }
        return res.ToString();
    }

    private void Write(uint address, byte value) {
        MonitorWriteAccess(address, value);
        _memoryDevices[address].Write(address, value);
    }

    private byte Read(uint address) {
        MonitorReadAccess(address);
        return _memoryDevices[address].Read(address);
    }

    private void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(uint address, byte value) {
        CurrentlyWritingByte = value;
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorRangeReadAccess(uint startAddress, uint endAddress) {
        _readBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private record DeviceRegistration(uint StartAddress, uint EndAddress, IMemoryDevice Device);

}

