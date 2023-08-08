namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction; 

public interface IDiscriminatorPart {
    /// <summary>
    /// Differs to value for fields which do not represent something that changes CPU logic.
    /// Opcode would have value and valueForDiscriminator with the same value.
    /// For example in MOV AX, 1234:
    ///  - 1234 would be a InstructionField<ushort>
    ///  - value would be 1234
    ///  - ValueForDiscriminator would be [null, null]
    /// </summary>
    public byte[] ValueForDiscriminator { get; }
}