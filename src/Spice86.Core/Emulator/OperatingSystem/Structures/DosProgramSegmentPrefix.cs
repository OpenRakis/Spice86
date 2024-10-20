namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Shared.Utils;

/// <summary>
/// Represents the Program Segment Prefix (PSP)
/// </summary>
public sealed class DosProgramSegmentPrefix : DosEnvironmentBlock {
    public DosProgramSegmentPrefix(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    public void MakeNew(ushort memSize) {

    }

    public void CloseFiles() {

    }

    /// <summary>
    /// Gets the <see cref="BaseAddress"/> of the PSP as a segment.
    /// </summary>
    public ushort Segment => MemoryUtils.ToSegment(BaseAddress);

    public void SaveVectors() {

    }

    public void RestoreVectors() {

    }

    public override string? GetEnvironmentVariable(string variableName) {
        throw new NotImplementedException();
    }

    public override void SetEnvironmentVariable(string variableName, string value) {
        throw new NotImplementedException();
    }

    public static ushort RootPspSegment { get; }

    /// <summary>
    /// CP/M like exit point.
    /// </summary>
    public byte[] Exit { get => new byte[] { UInt8[0], UInt8[1] }; set { UInt8[0] = value[0]; UInt8[1] = value[1]; } }

    /// <summary>
    /// Segment of first byte beyond the memory allocated or program.
    /// </summary>
    public ushort NextSegment { get => UInt16[1]; set => UInt16[1] = value; }
}
