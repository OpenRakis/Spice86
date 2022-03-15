namespace Spice86.Emulator.Memory;
public class UInt8IndexerWithUint {
    private Memory _memory;

    public UInt8IndexerWithUint(Memory memory) => _memory = memory;

    public ushort this[uint i] {
        get { return _memory.GetUint8(i); }
        set { _memory.SetUint8(i, (byte)value); }
    }
}
