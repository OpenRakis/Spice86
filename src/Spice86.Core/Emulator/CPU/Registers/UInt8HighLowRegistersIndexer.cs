namespace Spice86.Core.Emulator.CPU.Registers;

/// <summary>
/// Accesses registers bytes values based on a given index that represents a high/low byte.
/// </summary>
public class UInt8HighLowRegistersIndexer : RegistersIndexer<byte> {
    /// <summary>
    /// 3rd bit in register index means to access the high part
    /// </summary>
    private const int Register8IndexHighBitMask = 0b100;

    /// <summary>
    /// Registers allowing access to their high / low parts have indexes from 0 to 3 so 2 bits
    /// </summary>
    private const int Register8IndexHighLowMask = 0b11;

    private readonly UInt8HighRegistersIndexer _uInt8HighRegistersIndexer;
    private readonly UInt8LowRegistersIndexer _uInt8LowRegistersIndexer;


    public UInt8HighLowRegistersIndexer(UInt8HighRegistersIndexer uInt8HighRegistersIndexer,
        UInt8LowRegistersIndexer uInt8LowRegistersIndexer) {
        _uInt8HighRegistersIndexer = uInt8HighRegistersIndexer;
        _uInt8LowRegistersIndexer = uInt8LowRegistersIndexer;
    }

    public override byte this[uint index] {
        get {
            uint indexInArray = ComputeRegisterIndexInArray(index);
            if (IsHigh(index)) {
                return _uInt8HighRegistersIndexer[indexInArray];
            }

            return _uInt8LowRegistersIndexer[indexInArray];
        }
        set {
            uint indexInArray = ComputeRegisterIndexInArray(index);
            if (IsHigh(index)) {
                _uInt8HighRegistersIndexer[indexInArray] = value;
            } else {
                _uInt8LowRegistersIndexer[indexInArray] = value;
            }
        }
    }

    public bool IsHigh(uint index) {
        return (index & Register8IndexHighBitMask) != 0;
    }

    public uint ComputeRegisterIndexInArray(uint index) {
        return index & Register8IndexHighLowMask;
    }
}