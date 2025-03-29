namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using System.Collections.Immutable;

public abstract class FieldWithValue : Discriminator {
    public FieldWithValue(ImmutableList<byte?> discriminatorValue, bool final) : base(discriminatorValue) {
        Final = final;
    }

    /// <summary>
    /// Physical address of the field in memory
    /// </summary>
    public uint PhysicalAddress { get; init; }
    
    /// <summary>
    /// When true value can be used for execution.
    /// When false the value has to be retrieved from the memory location of the field because field value is modified by code.
    /// </summary>
    public bool UseValue { get; set; }

    /// <summary>
    /// Compares the positions and the value of this field with those of another field.
    /// </summary>
    /// <param name="other"></param>
    /// <returns>True if position and value is equals to the other field</returns>
    public abstract bool IsValueAndPositionEquals(FieldWithValue other);
    
    /// <summary>
    /// Length of this field
    /// </summary>
    public int Length { get; init;  }
    
    /// <summary>
    /// True means if the value of this field changes, enclosing instruction is not the same instruction anymore
    /// </summary>
    public bool Final { get; }
}