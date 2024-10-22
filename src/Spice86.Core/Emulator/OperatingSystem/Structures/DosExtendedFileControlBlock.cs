namespace Spice86.Core.Emulator.OperatingSystem.Structures;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure.Array;

public class DosExtendedFileControlBlock : DosFileControlBlock {
    public DosExtendedFileControlBlock(DosFileControlBlock dosFileControlBlock) : base(dosFileControlBlock.ByteReaderWriter, dosFileControlBlock.BaseAddress - 7) {
    }

    public byte Signature {
        get => UInt8[0];
        set => UInt8[0] = value;
    }

    public UInt8Array ExtendedReserved {
        get => GetUInt8Array(1, 5);
    }

    public const byte ExpectedSignature = 0xFF;

    public byte FileAttribute {
        get => UInt8[2];
        set => UInt8[2] = value;
    }
}
