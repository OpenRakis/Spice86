namespace Spice86.Emulator.Memory;
public class UShortIndexerWithUint {
    private Memory _memory;

    public UShortIndexerWithUint(Memory memory) => _memory = memory;

    public ushort this[uint i] {
        get { return _memory.GetUint16(i); }
        set { _memory.SetUint16(i, value); }
    }
}
