namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public sealed class DosSwappableDataArea : MemoryBasedDataStructure {
    public DosSwappableDataArea(IByteReaderWriter byteReaderWriter, uint baseAddress) : base(byteReaderWriter, baseAddress) {
    }

    /// <summary>
    /// Gets or sets the current drive. 0x0: A:, 0x1: B:, etc.
    /// </summary>
    public byte CurrentDrive { get; set; }

    /// <summary>
    /// Gets or sets the current disk transfer area
    /// </summary>
    public uint CurrentDiskTransferArea { get; set; }

    /// <summary>
    /// Gets or sets the current program segment prefix
    /// </summary>
    public ushort CurrentProgramSegmentPrefix { get; set; }
}
