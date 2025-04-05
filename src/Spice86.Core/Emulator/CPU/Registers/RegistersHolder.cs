namespace Spice86.Core.Emulator.CPU.Registers;

using Spice86.Core.Emulator.Memory.ReaderWriter;

using System.Collections.Frozen;

/// <summary>
/// A base class that represents a set of CPU registers.
/// </summary>
public class RegistersHolder {
    private readonly uint[] _registers;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistersHolder"/> class.
    /// </summary>
    /// <param name="count">The number of registers.</param>
    protected RegistersHolder(int count) {
        _registers = new uint[count];
        IUIntReaderWriter readerWriter = new UIntArrayReaderWriter(_registers);
        UInt32 = new(readerWriter);
        UInt16 = new(readerWriter);
        UInt8HighRegistersIndexer high = new(readerWriter);
        UInt8LowRegistersIndexer low = new(readerWriter);
        UInt8Low = low;
        UInt8High = high;
        UInt8HighLow = new(high, low);
    }

    public UInt8LowRegistersIndexer UInt8Low { get; }
    public UInt8HighRegistersIndexer UInt8High { get; }
    public UInt8HighLowRegistersIndexer UInt8HighLow { get; }
    public UInt16RegistersIndexer UInt16 { get; }
    public UInt32RegistersIndexer UInt32 { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj) {
        if (obj == this) {
            return true;
        }
        if (obj is not RegistersHolder other) {
            return false;
        }
        return _registers.AsSpan().SequenceEqual(other._registers);
    }

    /// <summary>
    /// Compute the hash code of the class instance.
    /// </summary>
    /// <returns>The combined hash of the instance, and the <see cref="_registers"/> array.</returns>
    public override int GetHashCode() {
        return _registers.GetHashCode();
    }
}
