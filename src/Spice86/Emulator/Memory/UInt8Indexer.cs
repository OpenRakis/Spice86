namespace Spice86.Emulator.Memory;
public class UInt8Indexer {
    private readonly Memory _memory;

    public UInt8Indexer(Memory memory) => _memory = memory;

    public byte this[uint i] {
        get { return _memory.GetUint8(i); }
        set { _memory.SetUint8(i, value); }
    }

    public byte this[ushort segment, ushort offset] {
        get { return this[MemoryUtils.ToPhysicalAddress(segment, offset)]; }
        set { this[MemoryUtils.ToPhysicalAddress(segment, offset)] = value; }
    }
}
