namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System.Diagnostics;

/// <summary>
/// Represents the DOS EXEC overlay parameter block used by INT 21h, AH=4Bh, AL=03h.
/// This structure is passed in ES:BX when calling the EXEC function with load overlay mode.
/// </summary>
/// <remarks>
/// Based on MS-DOS 4.0 EXEC.ASM and RBIL documentation.
/// <para>
/// For load overlay (AL=03h):
/// <code>
/// Offset  Size    Description
/// 00h     WORD    Segment at which to load the overlay
/// 02h     WORD    Relocation factor for EXE overlays (typically same as load segment)
/// </code>
/// </para>
/// </remarks>
[DebuggerDisplay("LoadSegment={LoadSegment}, RelocationFactor={RelocationFactor}")]
public class DosExecOverlayParameterBlock : MemoryBasedDataStructure {
    /// <summary>
    /// Size of the overlay parameter block in bytes.
    /// </summary>
    public const int Size = 0x04;

    /// <summary>
    /// Initializes a new instance of the overlay parameter block.
    /// </summary>
    /// <param name="byteReaderWriter">Where data is read and written.</param>
    /// <param name="baseAddress">The address of the structure in memory.</param>
    public DosExecOverlayParameterBlock(IByteReaderWriter byteReaderWriter, uint baseAddress)
        : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the segment at which to load the overlay.
    /// </summary>
    /// <remarks>Offset 0x00, 2 bytes.</remarks>
    public ushort LoadSegment { get => UInt16[0x00]; set => UInt16[0x00] = value; }

    /// <summary>
    /// Gets or sets the relocation factor for EXE overlays.
    /// This value is only used when loading EXE files.
    /// </summary>
    /// <remarks>Offset 0x02, 2 bytes.</remarks>
    public ushort RelocationFactor { get => UInt16[0x02]; set => UInt16[0x02] = value; }
}
