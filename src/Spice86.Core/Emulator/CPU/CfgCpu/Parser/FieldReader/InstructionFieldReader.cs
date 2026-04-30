namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Collections.Immutable;

public abstract class InstructionFieldReader<T> {
    protected IIndexable Memory { get; }

    protected InstructionReaderAddressSource AddressSource { get; }


    protected InstructionFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) {
        Memory = memory;
        AddressSource = addressSource;
    }

    protected SegmentedAddress CurrentAddress => AddressSource.CurrentAddress;

    protected uint CurrentPhysicalAddress => MemoryUtils.ToPhysicalAddress(CurrentAddress.Segment, CurrentAddress.Offset);

    protected abstract int FieldSize();

    public abstract T PeekValue();

    protected byte PeekUInt8(int offset) {
        SegmentedAddress address = CurrentAddress;
        uint effectiveOffset = (uint)(address.Offset + offset);
        return Memory.UInt8[address.Segment, effectiveOffset];
    }

    protected ushort PeekUInt16(int offset) {
        SegmentedAddress address = CurrentAddress;
        uint effectiveOffset = (uint)(address.Offset + offset);
        return Memory.UInt16[address.Segment, effectiveOffset];
    }

    protected uint PeekUInt32(int offset) {
        SegmentedAddress address = CurrentAddress;
        uint effectiveOffset = (uint)(address.Offset + offset);
        return Memory.UInt32[address.Segment, effectiveOffset];
    }

    public void Advance() {
        AddressSource.IndexInInstruction += FieldSize();
    }
    public void Recede() {
        AddressSource.IndexInInstruction -= FieldSize();
    }

    /// <summary>
    /// Reads field at current address.
    /// </summary>
    /// <param name="finalValue">If true, a change in value in memory will lead to the feeder to create a new instruction with the new value</param>
    /// <returns></returns>
    public virtual InstructionField<T> PeekField(bool finalValue) {
        T value = PeekValue();
        ImmutableList<byte?> bytes = PeekData(FieldSize());
        return new InstructionField<T>(AddressSource.IndexInInstruction, FieldSize(),
            CurrentPhysicalAddress, value, bytes, finalValue);
    }

    /// <summary>
    /// Reads field at current address and advance read address to next.
    /// </summary>
    /// <param name="finalValue">If true, a change in value in memory will lead to the feeder to create a new instruction with the new value</param>
    /// <returns></returns>
    public InstructionField<T> NextField(bool finalValue) {
        InstructionField<T> res = PeekField(finalValue);
        Advance();
        return res;
    }

    private ImmutableList<byte?> PeekData(int size) {
        ImmutableList<byte?>.Builder builder = ImmutableList.CreateBuilder<byte?>();
        for (int i = 0; i < size; i++) {
            builder.Add(PeekUInt8(i));
        }
        return builder.ToImmutable();
    }
}