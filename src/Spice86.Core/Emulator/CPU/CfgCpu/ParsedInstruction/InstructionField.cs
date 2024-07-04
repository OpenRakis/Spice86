namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Spice86.Core.Emulator.Memory.Indexable;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
/// Represents a field of an instruction.
/// 
/// </summary>
/// <typeparam name="T"></typeparam>
public class InstructionField<T> : FieldWithValue {
    /// <summary>
    /// Index of the field in the enclosing instruction
    /// </summary>
    public int IndexInInstruction { get; }

    public InstructionField(int indexInInstruction, int length, uint physicalAddress, T value,
        ImmutableList<byte?> discriminatorValue, bool final) : base(discriminatorValue, final) {
        IndexInInstruction = indexInInstruction;
        Length = length;
        PhysicalAddress = physicalAddress;
        Value = value;
        // By default no reason to not use the value at parse time
        UseValue = true;
    }

    /// <summary>
    /// Physical address of the field in memory
    /// </summary>
    public uint PhysicalAddress { get; }

    /// <summary>
    /// Value of the field at creation time. Meaningless if UseValue is false.
    /// Differs to discriminator for fields which do not represent something that changes CPU logic.
    /// For example in MOV AX, 1234:
    ///  - 1234 would be a InstructionField<ushort>
    ///  - value would be 1234
    ///  - ValueForDiscriminator would be [null, null]
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Compares the positions and the value of this field with those of another field.
    /// </summary>
    /// <param name="other"></param>
    /// <returns>True if position and value is equals to the other field</returns>
    public override bool IsValueAndPositionEquals(FieldWithValue other) {
        if (other is InstructionField<T> otherT) {
            return this.PhysicalAddress == otherT.PhysicalAddress && this.Length == otherT.Length &&
                   this.IndexInInstruction == otherT.IndexInInstruction &&
                   EqualityComparer<T>.Default.Equals(this.Value, otherT.Value);
        }

        return false;
    }
}