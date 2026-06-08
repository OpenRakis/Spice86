namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.Memory.Indexer;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Utils;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents the memory bus of the IBM PC.
/// </summary>
public sealed class Memory : Indexable.Indexable, IMemory {
    /// <inheritdoc/>
    public IMemoryDevice Ram { get; }

    private readonly AddressReadWriteBreakpoints _memoryBreakpoints;
    private IMemoryDevice[] _memoryDevices;
    private readonly List<DeviceRegistration> _devices = new();

    /// <summary>
    /// Represents the optional 20th address line suppression feature for legacy 8086 programs.
    /// </summary>
    public A20Gate A20Gate { get; }

    /// <inheritdoc />
    public IMmu Mmu { get; }

    /// <summary>
    /// Instantiate a new memory bus.
    /// </summary>
    /// <param name="memoryBreakpoints">The class that holds breakpoints based on memory access.</param>
    /// <param name="baseMemory">The memory device that should provide the default memory implementation</param>
    /// <param name="a20gate">The class that implements A20 Gate on/off support.</param>
    /// <param name="mmu">The MMU used for segmented access validation and translation.</param>
    /// <param name="initializeResetVector">Whether to initialize the reset vector with a HLT instruction.</param>
    public Memory(AddressReadWriteBreakpoints memoryBreakpoints, IMemoryDevice baseMemory, A20Gate a20gate, IMmu mmu,
        bool initializeResetVector) {
        _memoryBreakpoints = memoryBreakpoints;
        Mmu = mmu;
        uint memorySize = baseMemory.Size;
        _memoryDevices = new IMemoryDevice[memorySize];
        Ram = baseMemory;
        RegisterMapping(0, memorySize, Ram);
        (UInt8, UInt16, UInt16BigEndian, UInt32, Int8, Int16, Int32, SegmentedAddress16, SegmentedAddress32) =
            InstantiateIndexersFromByteReaderWriter(this, Mmu);
        A20Gate = a20gate;
    }

    /// <inheritdoc />
    public byte[] ReadRam(uint length = 0, uint offset = 0) {
        IMemoryDevice[] memoryDevices = _memoryDevices;
        if (offset >= (uint)memoryDevices.Length) {
            return [];
        }

        if (length == 0) {
            length = uint.MaxValue;
        }

        length = Math.Min(length, (uint)memoryDevices.Length - offset);
        byte[] copy = new byte[length];
        for (uint address = 0; address < copy.Length; address++) {
            uint elementAddress = address + offset;
            copy[address] = memoryDevices[elementAddress].Read(elementAddress);
        }

        return copy;
    }

    public int ReadRam(Span<byte> span, uint offset = 0) {
        IMemoryDevice[] memoryDevices = _memoryDevices;
        if (offset >= (uint)memoryDevices.Length) {
            return 0;
        }

        int length = Math.Min(span.Length, (int)((uint)memoryDevices.Length - offset));
        Debug.Assert(length <= (uint)span.Length);
        for (int i = 0; i < length; i++) {
            uint elementAddress = (uint)i + offset;
            span[i] = memoryDevices[elementAddress].Read(elementAddress);
        }

        return length;
    }

    /// <inheritdoc />
    public void WriteRam(byte[] array, uint offset = 0) {
        IMemoryDevice[] memoryDevices = _memoryDevices;
        if (offset >= memoryDevices.Length) {
            return;
        }

        uint length = Math.Min((uint)array.Length, (uint)memoryDevices.Length - offset);
        for (uint address = 0; address < length; address++) {
            uint elementAddress = address + offset;
            memoryDevices[elementAddress].Write(elementAddress, array[address]);
        }
    }

    public int WriteRam(ReadOnlySpan<byte> span, uint offset = 0) {
        IMemoryDevice[] memoryDevices = _memoryDevices;
        if (offset >= memoryDevices.Length) {
            return 0;
        }

        int length = Math.Min(span.Length, (int)((uint)memoryDevices.Length - offset));
        for (int address = 0; address < length; address++) {
            uint elementAddress = (uint)address + offset;
            memoryDevices[elementAddress].Write(elementAddress, span[address]);
        }

        return length;
    }

    /// <inheritdoc />
    public byte SneakilyRead(uint address) {
        return _memoryDevices[address].Read(address);
    }

    /// <inheritdoc />
    public void SneakilyWrite(uint address, byte value) {
        _memoryDevices[address].Write(address, value);
    }

    /// <inheritdoc />
    public void WriteUInt16Segmented(ushort segment, ushort offset, ushort value) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(ushort), out uint address)) {
            this[address] = (byte)value;
            this[address + 1] = (byte)(value >>> 8);
        } else {
            this[Mmu.TranslateAddress(segment, offset)] = (byte)value;
            this[Mmu.TranslateAddress(segment, offset + 1u)] = (byte)(value >> 8);
        }
    }

    /// <inheritdoc />
    public void WriteUInt32Segmented(ushort segment, ushort offset, uint value) {
        if (Mmu.TryTranslateAddressRange(segment, offset, sizeof(uint), out uint address)) {
            this[address] = (byte)value;
            this[address + 1] = (byte)(value >>> 8);
            this[address + 2] = (byte)(value >>> 16);
            this[address + 3] = (byte)(value >>> 24);
        } else {
            this[Mmu.TranslateAddress(segment, offset)] = (byte)value;
            this[Mmu.TranslateAddress(segment, offset + 1u)] = (byte)(value >> 8);
            this[Mmu.TranslateAddress(segment, offset + 2u)] = (byte)(value >> 16);
            this[Mmu.TranslateAddress(segment, offset + 3u)] = (byte)(value >> 24);
        }
    }

    /// <inheritdoc/>
    public byte this[uint address] {
        get {
            address = A20Gate.TransformAddress(address);
            _memoryBreakpoints.MonitorReadAccess(address);
            return SneakilyRead(address);
        }
        set {
            address = A20Gate.TransformAddress(address);
            CurrentlyWritingByte = value;
            _memoryBreakpoints.MonitorWriteAccess(address);
            SneakilyWrite(address, value);
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
    public int Length => _memoryDevices.Length;

    public IList<byte> GetSlice(int address, int length) {
        return UInt8.GetSlice(address, length);
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
    public sealed override UInt8Indexer UInt8 {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt16Indexer UInt16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt16BigEndianIndexer UInt16BigEndian {
        get;
    }

    /// <inheritdoc/>
    public sealed override UInt32Indexer UInt32 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int8Indexer Int8 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int16Indexer Int16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override Int32Indexer Int32 {
        get;
    }

    /// <inheritdoc/>
    public sealed override SegmentedAddress16Indexer SegmentedAddress16 {
        get;
    }

    /// <inheritdoc/>
    public sealed override SegmentedAddress32Indexer SegmentedAddress32 {
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

#if true
    public bool TryGetSpan(uint startAddress, int length, out Span<byte> span, MemoryAccess access) {
        // Invalid span request if length is negative.
        if (length < 0) {
            span = [];
            return false;
        }

        // Return an empty span if zero.
        if (length == 0) {
            span = [];
            return true;
        }

        // Transform start and end addresses through A20 gate.
        // Note that the end address is inclusive (to avoid issues with wrapped addresses on the last byte).
        startAddress = A20Gate.TransformAddress(startAddress);
        uint endAddress = A20Gate.TransformAddress(startAddress + (uint)length - 1);
        length = (int)(endAddress - startAddress + 1);

        if (length > 0
            && TryGetDevice(startAddress, length, out IMemoryDevice? device)
            && device.TryGetSpan(startAddress, length, out Span<byte> dataSpan, access)
            && !HasActiveBreakPoints(startAddress, length, access)) {
            span = dataSpan;
            return true;
        }

        span = [];
        return false;
    }

    public bool TryGetSpan(uint startAddress, int length, out ReadOnlySpan<byte> span, MemoryAccess access) {
        // Invalid span request if length is negative.
        if (length < 0) {
            span = [];
            return false;
        }

        // Return an empty span if zero.
        if (length == 0) {
            span = [];
            return true;
        }

        // Transform start and end addresses through A20 gate.
        // Note that the end address is inclusive (to avoid issues with wrapped addresses on the last byte).
        startAddress = A20Gate.TransformAddress(startAddress);
        uint endAddress = A20Gate.TransformAddress(startAddress + (uint)length - 1);
        length = (int)(endAddress - startAddress + 1);

        if (length > 0
            && TryGetDevice(startAddress, length, out IMemoryDevice? device)
            && device.TryGetSpan(startAddress, length, out ReadOnlySpan<byte> dataSpan, access)
            && !HasActiveBreakPoints(startAddress, length, access)) {
            span = dataSpan;
            return true;
        }

        span = [];
        return false;
    }

    private bool TryGetDevice(uint startAddress, int length, [MaybeNullWhen(false)] out IMemoryDevice device) {
        IMemoryDevice[] memoryDevices = _memoryDevices;
        IMemoryDevice tempDevice = memoryDevices[startAddress];
        if (memoryDevices.AsSpan((int)startAddress, length).ContainsAnyExcept(tempDevice)) {
            device = null;
            return false;
        }

        device = tempDevice;
        return true;
    }

    private bool HasActiveBreakPoints(uint startAddress, int length, MemoryAccess access) {
        // Ignore all flags except read/write.
        access &= MemoryAccess.ReadWrite;
        if (access == MemoryAccess.None) {
            // Bypass breakpoint check when no read/write access is requested.
            return false;
        }

        AddressOperation breakPointTrigger = access switch {
            MemoryAccess.Read => AddressOperation.READ,
            MemoryAccess.Write => AddressOperation.WRITE,
            _ => AddressOperation.ACCESS,
        };

        // TODO: This is slow, it would be nice if there was a cache/lookup table of active break points.
        // TODO: This does not check "dynamic" breakpoints that have custom trigger conditions defined.
        AddressReadWriteBreakpoints memoryBreakpoints = _memoryBreakpoints;
        for (int i = 0; i < length; i++) {
            if (memoryBreakpoints.HasActiveBreakPoint(startAddress + (uint)i, breakPointTrigger)) {
                return true;
            }
        }

        return false;
    }
#endif
}