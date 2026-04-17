namespace Spice86.Core.Emulator.InterruptHandlers.Common.MemoryWriter;

/// <summary>
/// Builder for constructing x86 instruction sequences directly into a byte list.
/// Useful for generating COM stubs and in-memory program code without a SegmentedAddress context.
/// Each method encapsulates x86 instruction opcodes and operands for better readability.
/// </summary>
public class X86InstructionBuilder {
    private readonly List<byte> _code = new();

    /// <summary>
    /// Gets the instruction sequence as a byte array.
    /// </summary>
    public byte[] ToArray() => _code.ToArray();

    /// <summary>
    /// Gets the current byte count.
    /// </summary>
    public int Count => _code.Count;

    /// <summary>
    /// Gets or sets a byte at the specified index.
    /// </summary>
    public byte this[int index] {
        get => _code[index];
        set => _code[index] = value;
    }

    /// <summary>
    /// Adds a raw byte to the sequence.
    /// </summary>
    public void Add(byte b) => _code.Add(b);

    /// <summary>
    /// Adds a range of bytes to the sequence.
    /// </summary>
    public void AddRange(byte[] bytes) => _code.AddRange(bytes);

    /// <summary>
    /// Writes MOV AH, imm8 instruction.
    /// </summary>
    public void WriteMovAh(byte value) {
        Add(0xB4);
        Add(value);
    }

    /// <summary>
    /// Writes MOV AX, imm16 instruction.
    /// </summary>
    public void WriteMovAx(ushort value) {
        Add(0xB8);
        Add((byte)(value & 0xFF));
        Add((byte)(value >> 8));
    }

    /// <summary>
    /// Writes MOV DX, imm16 instruction with placeholder for patching.
    /// </summary>
    /// <returns>The offset of the second byte (first placeholder).</returns>
    public int WriteMovDxWithPlaceholder() {
        int patchOffset = Count;
        Add(0xBA);   // MOV DX, imm16
        Add(0x00);   //   imm16 lo (patched later)
        Add(0x00);   //   imm16 hi (patched later)
        return patchOffset;
    }

    /// <summary>
    /// Writes CMP AL, imm8 instruction.
    /// </summary>
    public void WriteCmpAl(byte value) {
        Add(0x3C);
        Add(value);
    }

    /// <summary>
    /// Writes CMP AH, imm8 instruction.
    /// </summary>
    public void WriteCmpAh(byte value) {
        Add(0x80);
        Add(0xFC);
        Add(value);
    }

    /// <summary>
    /// Writes JE (Jump if Equal) instruction, returns offset for patching.
    /// </summary>
    /// <returns>The offset of the displacement byte.</returns>
    public int WriteJe() {
        Add(0x74);
        int dispOffset = Count;
        Add(0x00);
        return dispOffset;
    }

    /// <summary>
    /// Writes JA (Jump if Above) instruction, returns offset for patching.
    /// </summary>
    public int WriteJa() {
        Add(0x77);
        int dispOffset = Count;
        Add(0x00);
        return dispOffset;
    }

    /// <summary>
    /// Writes JB (Jump if Below) instruction, returns offset for patching.
    /// </summary>
    public int WriteJb() {
        Add(0x72);
        int dispOffset = Count;
        Add(0x00);
        return dispOffset;
    }

    /// <summary>
    /// Writes JMP SHORT instruction.
    /// </summary>
    public void WriteJmpShort(int displacement) {
        Add(0xEB);
        Add((byte)displacement);
    }

    /// <summary>
    /// Writes SUB AL, imm8 instruction.
    /// </summary>
    public void WriteSubAl(byte value) {
        Add(0x2C);
        Add(value);
    }

    /// <summary>
    /// Writes INT instruction.
    /// </summary>
    public void WriteInt(byte vectorNumber) {
        Add(0xCD);
        Add(vectorNumber);
    }

    /// <summary>
    /// Writes MOV AX, imm16 with split AL and AH bytes.
    /// </summary>
    public void WriteMovAxSplit(byte alValue, byte ahValue) {
        Add(0xB8);
        Add(alValue);
        Add(ahValue);
    }

    /// <summary>
    /// Writes IRET instruction.
    /// </summary>
    public void WriteIret() {
        Add(0xCF);
    }

    /// <summary>
    /// Writes NOP instruction.
    /// </summary>
    public void WriteNop() {
        Add(0x90);
    }
}
