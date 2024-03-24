namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

using System.Collections.Immutable;
using System.Linq;

public abstract class InstructionFieldReader<T> {
    protected IIndexable Memory { get; }

    protected InstructionReaderAddressSource AddressSource { get; }


    protected InstructionFieldReader(IIndexable memory, InstructionReaderAddressSource addressSource) {
        Memory = memory;
        AddressSource = addressSource;
    }

    protected SegmentedAddress CurrentAddress => AddressSource.CurrentAddress;

    protected abstract int FieldSize();
    public abstract T PeekValue();

    public void Advance() {
        AddressSource.IndexInInstruction += FieldSize();
    }

    public InstructionField<T> PeekField(bool nonNullDiscriminator) {
        T value = PeekValue();
        ImmutableList<byte?> bytes = PeekDataOrNullList(FieldSize(), nonNullDiscriminator);
        return new InstructionField<T>(AddressSource.IndexInInstruction, FieldSize(),
            CurrentAddress.ToPhysical(), value, bytes);
    }

    public InstructionField<T> NextField(bool nonNullDiscriminator) {
        InstructionField<T> res = PeekField(nonNullDiscriminator);
        Advance();
        return res;
    }

    private ImmutableList<byte?> PeekDataOrNullList(int size, bool data) {
        if (data) {
            return PeekData(size);
        }
        return GenerateNullBytes(size);
    }
    
    private static ImmutableList<byte?> GenerateNullBytes(int size) {
        List<byte?> res = new List<byte?>();
        for (int i = 0; i < size; i++) {
            res.Add(null);
        }
        return ImmutableList.CreateRange(res);
    }

    private ImmutableList<byte?> PeekData(int size) {
        byte[] data = Memory.GetData(CurrentAddress.ToPhysical(), (uint)size);
        return data.
            Select(b => (byte?)b).
            ToImmutableList();
    }
}