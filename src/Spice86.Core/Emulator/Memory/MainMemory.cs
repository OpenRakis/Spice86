namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM;

/// <summary>
/// Represents the main memory of the IBM PC.
/// Size must be at least 1 MB.
/// </summary>
public class MainMemory : Memory {
    /// <summary>
    /// Size of conventional memory in bytes.
    /// </summary>
    public const uint ConvMemorySize = 1024 * 1024;

    private readonly Func<uint, byte>[] _getUint8Functions;
    private readonly Func<uint, ushort>[] _getUint16Functions;
    private readonly Func<uint, uint>[] _getUint32Functions;
    private readonly Action<uint, byte>[] _setUint8Functions;
    private readonly Action<uint, ushort>[] _setUint16Functions;
    private readonly Action<uint, uint>[] _setUint32Functions;
    private readonly uint _segmentCount;

    public MainMemory(Machine machine, uint sizeInKb) : base(sizeInKb) {
        if (sizeInKb * 1024 < ConvMemorySize) {
            throw new ArgumentException("Memory size must be at least 1 MB.");
        }
        _segmentCount = sizeInKb / 64 + 1;
        _getUint8Functions = new Func<uint, byte>[_segmentCount];
        _getUint16Functions = new Func<uint, ushort>[_segmentCount];
        _getUint32Functions = new Func<uint, uint>[_segmentCount];
        _setUint8Functions = new Action<uint, byte>[_segmentCount];
        _setUint16Functions = new Action<uint, ushort>[_segmentCount];
        _setUint32Functions = new Action<uint, uint>[_segmentCount];

        for (int i = 0; i < _getUint8Functions.Length; i++) {
            _getUint8Functions[i] = address => base.GetUint8(address);
            _getUint16Functions[i] = address => base.GetUint16(address);
            _getUint32Functions[i] = address => base.GetUint8(address);
            _setUint8Functions[i] = (address, value) => base.SetUint8(address, value);
            _setUint16Functions[i] = (address, value) => base.SetUint16(address, value);
            _setUint32Functions[i] = (address, value) => base.SetUint32(address, value);
        }
    }

    public void RegisterMapping(uint segment, Memory memory) {
        uint block = segment >> 12;
        if (block >= _segmentCount) {
            throw new ArgumentException($"Segment {segment} out of range 0-{_segmentCount - 1}.");
        }
        _getUint8Functions[block] = memory.GetUint8;
        _getUint16Functions[block] = memory.GetUint16;
        _getUint32Functions[block] = memory.GetUint32;
        _setUint8Functions[block] = memory.SetUint8;
        _setUint16Functions[block] = memory.SetUint16;
        _setUint32Functions[block] = memory.SetUint32;
    }

    public override void SetUint32(uint address, uint value) => _setUint32Functions[address >> 16](address, value);
    public override void SetUint16(uint address, ushort value) => _setUint16Functions[address >> 16](address, value);
    public override void SetUint8(uint address, byte value) => _setUint8Functions[address >> 16](address, value);
    public override uint GetUint32(uint address) => _getUint32Functions[address >> 16](address);
    public override ushort GetUint16(uint address) => _getUint16Functions[address >> 16](address);
    public override byte GetUint8(uint address) => _getUint8Functions[address >> 16](address);
}