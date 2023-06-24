namespace Spice86.Core.Emulator.Memory;

public interface IMemoryStore {
    public byte GetUint8(uint address);

    public uint GetUint32(uint address);

    public Span<byte> GetSpan(int address, int length);

    public byte[] GetData(uint address, uint length);

    public ushort GetUint16(uint address);

    public string GetZeroTerminatedString(uint address, int maxLength);

    public void SetUint32(uint address, uint value);
    
    public void SetUint16(uint address, ushort value);

    public void SetUint8(uint address, byte value);

    public void SetZeroTerminatedString(uint address, string value, int maxLength);
}