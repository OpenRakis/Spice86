namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Memory.Indexer;

/// <summary>
/// Represents the memory bus of the IBM PC.
/// </summary>
public class Memory : Indexable.Indexable, IMemory {
    /// <inheritdoc/>
    public IMemoryDevice Ram { get; }

    private readonly MemoryBreakpoints _memoryBreakpoints;
    private IMemoryDevice[] _memoryDevices;
    private readonly List<DeviceRegistration> _devices = new();

    /// <summary>
    /// Represents the optional 20th address line suppression feature for legacy 8086 programs.
    /// </summary>
    public A20Gate A20Gate { get; }

    /// <summary>
    /// Instantiate a new memory bus.
    /// </summary>
    /// <param name="memoryBreakpoints">The class that holds breakpoints based on memory access.</param>
    /// <param name="baseMemory">The memory device that should provide the default memory implementation</param>
    /// <param name="a20gate">The class that implements A20 Gate on/off support.</param>
    public Memory(MemoryBreakpoints memoryBreakpoints, IMemoryDevice baseMemory, A20Gate a20gate) {
        _memoryBreakpoints = memoryBreakpoints;
        uint memorySize = baseMemory.Size;
        _memoryDevices = new IMemoryDevice[memorySize];
        Ram = baseMemory;
        RegisterMapping(0, memorySize, Ram);
        (UInt8, UInt16, UInt16BigEndian, UInt32, Int8, Int16, Int32, SegmentedAddress) = InstantiateIndexersFromByteReaderWriter(this);
        A20Gate = a20gate;
    }

    /// <summary>
    /// This is the start of the HMA. <br/>
    /// This value is equal to 1 MB.
    /// </summary>
    public const uint StartOfHighMemoryArea = 0x100000;

    /// <summary>
    /// This is the end of the HMA. <br/>
    /// Real Mode cannot access memory beyond this. <br/>
    /// This value equals to 1 MB + 65 519 bytes.
    /// </summary>
    public const uint EndOfHighMemoryArea = 0x10FFEF;

    /// <inheritdoc />
    public byte[] ReadRam(uint length = 0, uint offset = 0) {
        if (length == 0) {
            length = (uint)_memoryDevices.Length;
        }
        byte[] copy = new byte[length];
        for (uint address = 0; address < copy.Length; address++) {
            copy[address] = _memoryDevices[address + offset].Read(address + offset);
        }
        return copy;
    }
    
    /// <inheritdoc />
    public void WriteRam(byte[] array, uint offset = 0) {
        for (uint address = 0; address < array.Length; address++) {
            _memoryDevices[address + offset].Write(address + offset, array[address]);
        }
    }

    /// <inheritdoc/>
    public byte this[uint address] {
        get {
            address = A20Gate.TransformAddress(address);
            _memoryBreakpoints.MonitorReadAccess(address);
            return _memoryDevices[address].Read(address);
        }
        set {
            address = A20Gate.TransformAddress(address);
            CurrentlyWritingByte = value;
            _memoryBreakpoints.MonitorWriteAccess(address);
            _memoryDevices[address].Write(address, value);
        }
    }

    /// <summary>
    ///     Allows memory write breakpoints to access the byte being written before it actually is.
    /// </summary>
    public byte CurrentlyWritingByte {
        get;
        private set;
    }

    /// <inheritdoc/>
    public uint Length => (uint)_memoryDevices.Length;

    /// <summary>
    /// Returns a <see cref="Span{T}"/> that represents the specified range of memory.
    /// </summary>
    /// <param name="address">The starting address of the memory range.</param>
    /// <param name="length">The length of the memory range.</param>
    /// <returns>A <see cref="Span{T}"/> instance that represents the specified range of memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no memory device supports the specified memory range.</exception>
    public Span<byte> GetSpan(int address, int length) {
        address = A20Gate.TransformAddress(address);
        foreach (DeviceRegistration device in _devices) {
            if (address < device.StartAddress || address + length > device.EndAddress) {
                continue;
            }
            _memoryBreakpoints.MonitorRangeReadAccess((uint)address, (uint)(address + length));
            return device.Device.GetSpan(address, length);
        }

        throw new InvalidOperationException($"No Memory Device supports a span from {address} to {address + length}");
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

    /// <inheritdoc/>
    public override UInt8Indexer UInt8 {
        get;
    }

    /// <inheritdoc/>
    public override UInt16Indexer UInt16 {
        get;
    }
    
    /// <inheritdoc/>
    public override UInt16BigEndianIndexer UInt16BigEndian {
        get;
    }

    /// <inheritdoc/>
    public override UInt32Indexer UInt32 {
        get;
    }

    /// <inheritdoc/>
    public override Int8Indexer Int8 {
        get;
    }

    /// <inheritdoc/>
    public override Int16Indexer Int16 {
        get;
    }

    /// <inheritdoc/>
    public override Int32Indexer Int32 {
        get;
    }

    /// <inheritdoc/>
    public override SegmentedAddressIndexer SegmentedAddress {
        get;
    }

    /// <inheritdoc/>
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

    private record DeviceRegistration(uint StartAddress, uint EndAddress, IMemoryDevice Device);
}