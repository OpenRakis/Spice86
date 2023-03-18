namespace Spice86.Core.Emulator.Memory;

/// <summary>
///   Represents the main memory of the IBM PC.
/// </summary>
public class MainMemory : Memory {
    private readonly uint _memorySize;
    private readonly Func<uint, byte>[] _getUint8Functions;
    private readonly Func<uint, ushort>[] _getUint16Functions;
    private readonly Func<uint, uint>[] _getUint32Functions;
    private readonly Action<uint, byte>[] _setUint8Functions;
    private readonly Action<uint, ushort>[] _setUint16Functions;
    private readonly Action<uint, uint>[] _setUint32Functions;

    public MainMemory(uint sizeInKb) : base(sizeInKb) {
        _memorySize = sizeInKb * 1024;
        _getUint8Functions = new Func<uint, byte>[_memorySize];
        _getUint16Functions = new Func<uint, ushort>[_memorySize];
        _getUint32Functions = new Func<uint, uint>[_memorySize];
        _setUint8Functions = new Action<uint, byte>[_memorySize];
        _setUint16Functions = new Action<uint, ushort>[_memorySize];
        _setUint32Functions = new Action<uint, uint>[_memorySize];

        for (uint i = 0; i < _memorySize; i++) {
            _getUint8Functions[i] = address => base.GetUint8(address);
            _setUint8Functions[i] = (address, value) => base.SetUint8(address, value);
            uint alignment = i % 4;
            switch (alignment) {
                case 0:
                    _getUint16Functions[i] = address => base.GetUint16(address);
                    _getUint32Functions[i] = address => base.GetUint32(address);
                    _setUint16Functions[i] = (address, value) => base.SetUint16(address, value);
                    _setUint32Functions[i] = (address, value) => base.SetUint32(address, value);
                    break;
                case 1:
                case 3:
                    _getUint16Functions[i] = GetUnalignedUint16;
                    _getUint32Functions[i] = GetUnalignedUint32;
                    _setUint16Functions[i] = SetUnalignedUint16;
                    _setUint32Functions[i] = SetUnalignedUint32;
                    break;
                case 2:
                    _getUint16Functions[i] = address => base.GetUint16(address);
                    _getUint32Functions[i] = GetUnalignedUint32;
                    _setUint16Functions[i] = (address, value) => base.SetUint16(address, value);
                    _setUint32Functions[i] = SetUnalignedUint32;
                    break;
                default:
                    throw new InvalidOperationException("Invalid alignment.");
            }
        }
    }

    private void SetUnalignedUint16(uint address, ushort value) {
        SetUint8(address + 0, (byte)(value & 0xFF));
        SetUint8(address + 1, (byte)(value >> 8));
    }

    private void SetUnalignedUint32(uint address, uint value) {
        SetUint8(address + 0, (byte)(value & 0xFF));
        SetUint8(address + 1, (byte)(value >> 8 & 0xFF));
        SetUint8(address + 2, (byte)(value >> 16 & 0xFF));
        SetUint8(address + 3, (byte)(value >> 24));
    }

    private ushort GetUnalignedUint16(uint address) {
        return (ushort)(GetUint8(address) | GetUint8(address + 1) << 8);
    }

    private uint GetUnalignedUint32(uint address) {
        return (uint)(GetUint8(address) | GetUint8(address + 1) << 8 | GetUint8(address + 2) << 16 | GetUint8(address + 3) << 24);
    }

    /// <summary>
    /// Allow another class to supply their own memory for a certain range.
    /// </summary>
    /// <param name="baseAddress">The start of the frame</param>
    /// <param name="size">The size of the window</param>
    /// <param name="memory">The memory instance to use</param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public void RegisterMapping(uint baseAddress, uint size, Memory memory) {
        if (baseAddress + size >= _memorySize) {
            throw new ArgumentException("Mapping is out of memory range.");
        }
        if (size > memory.Size) {
            throw new ArgumentOutOfRangeException(nameof(size), "Mapping size is larger than the memory size.");
        }
        if (baseAddress % 4 != 0) {
            throw new ArgumentOutOfRangeException(nameof(baseAddress), "Mapping base address must be aligned on a 4-byte boundary.");
        }
        if (size < 4) {
            throw new ArgumentOutOfRangeException(nameof(size), "Mapping size must be at least 4 bytes.");
        }
        if (size % 4 != 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "Mapping size must be a multiple of 4 bytes.");
        }
        for (uint i = baseAddress; i < size + baseAddress; i++) {
            _getUint8Functions[i] = memory.GetUint8;
            _setUint8Functions[i] = memory.SetUint8;
            uint alignment = i % 4;
            switch (alignment) {
                case 0:
                    _getUint16Functions[i] = memory.GetUint16;
                    _getUint32Functions[i] = memory.GetUint32;
                    _setUint16Functions[i] = memory.SetUint16;
                    _setUint32Functions[i] = memory.SetUint32;
                    break;
                case 1:
                case 3:
                    _getUint16Functions[i] = GetUnalignedUint16;
                    _getUint32Functions[i] = GetUnalignedUint32;
                    _setUint16Functions[i] = SetUnalignedUint16;
                    _setUint32Functions[i] = SetUnalignedUint32;
                    break;
                case 2:
                    _getUint16Functions[i] = memory.GetUint16;
                    _getUint32Functions[i] = GetUnalignedUint32;
                    _setUint16Functions[i] = memory.SetUint16;
                    _setUint32Functions[i] = SetUnalignedUint32;
                    break;
                default:
                    throw new InvalidOperationException("Invalid alignment.");
            }
        }
    }

    /// <summary>
    /// Writes a 4-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public override void SetUint32(uint address, uint value) {
        _setUint32Functions[address](address, value);
    }

    /// <summary>
    /// Writes a 2-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public override void SetUint16(uint address, ushort value) {
        _setUint16Functions[address](address, value);
    }

    /// <summary>
    /// Writes a 1-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public override void SetUint8(uint address, byte value) {
        _setUint8Functions[address](address, value);
    }

    /// <summary>
    /// Read a 4-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public override uint GetUint32(uint address) {
        return _getUint32Functions[address](address);
    }

    /// <summary>
    /// Read a 2-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public override ushort GetUint16(uint address) {
        return _getUint16Functions[address](address);
    }

    /// <summary>
    /// Read a 1-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public override byte GetUint8(uint address) {
        return _getUint8Functions[address](address);
    }
}