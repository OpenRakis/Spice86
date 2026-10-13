namespace Spice86.Core.Emulator.CPU.DescriptorTables;

/// <summary>
/// The raw 8-byte layout of an IDT gate descriptor (interrupt gate, trap gate, or task gate).
/// Distinct from <see cref="RawSegmentDescriptor"/>: a gate redirects control transfer rather than
/// describing an addressable memory segment.
/// </summary>
public readonly struct RawGateDescriptor {
    /// <summary>
    /// Decodes a gate descriptor from its raw 8-byte in-memory representation.
    /// </summary>
    /// <param name="descriptorBytes">The 8 raw descriptor bytes, in the order they appear in memory.</param>
    public RawGateDescriptor(ReadOnlySpan<byte> descriptorBytes) {
        if (descriptorBytes.Length != 8) {
            throw new ArgumentException("A gate descriptor is exactly 8 bytes.", nameof(descriptorBytes));
        }

        ushort offsetLow = (ushort)(descriptorBytes[0] | (descriptorBytes[1] << 8));
        ushort offsetHigh = (ushort)(descriptorBytes[6] | (descriptorBytes[7] << 8));
        byte typeByte = descriptorBytes[5];

        Selector = (ushort)(descriptorBytes[2] | (descriptorBytes[3] << 8));
        Offset = (uint)(offsetLow | (offsetHigh << 16));
        GateType = (GateType)(typeByte & 0x0F);
        DescriptorPrivilegeLevel = (byte)((typeByte >> 5) & 0b11);
        Present = (typeByte & 0x80) != 0;
    }

    /// <summary>The 32-bit offset of the handler entry point within its code segment.</summary>
    public uint Offset { get; }

    /// <summary>The code segment selector (interrupt/trap gates) or TSS selector (task gates).</summary>
    public ushort Selector { get; }

    /// <summary>The gate type (call, interrupt, trap, or task gate).</summary>
    public GateType GateType { get; }

    /// <summary>The descriptor privilege level required to invoke this gate via a software INT.</summary>
    public byte DescriptorPrivilegeLevel { get; }

    /// <summary>Whether the gate's present bit is set.</summary>
    public bool Present { get; }
}
