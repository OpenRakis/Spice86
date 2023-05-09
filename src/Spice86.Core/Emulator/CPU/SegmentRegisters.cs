namespace Spice86.Core.Emulator.CPU;

using System.Collections.Generic;

/// <summary>
/// A class that represents the segment registers of a CPU. <br/>
/// The segment registers are registers that store segment selectors, which are used to access different parts of memory.
/// </summary>
public class SegmentRegisters : RegistersHolder {
    /// <summary>
    /// The index of the CS (code segment) register.
    /// </summary>
    public const int CsIndex = 1;

    /// <summary>
    /// The index of the DS (data segment) register.
    /// </summary>
    public const int DsIndex = 3;

    /// <summary>
    /// The index of the ES (extra segment) register.
    /// </summary>
    public const int EsIndex = 0;

    /// <summary>
    /// The index of the FS register.
    /// </summary>
    public const int FsIndex = 4;

    /// <summary>
    /// The index of the GS register.
    /// </summary>
    public const int GsIndex = 5;

    /// <summary>
    /// The index of the SS (stack segment) register.
    /// </summary>
    public const int SsIndex = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentRegisters"/> class with the segment register names.
    /// </summary>
    public SegmentRegisters() : base(GetRegistersNames()) {
    }

    /// <summary>
    /// Gets a dictionary that maps the segment register index to its name.
    /// </summary>
    /// <returns>A dictionary that maps the segment register index to its name.</returns>
    private static Dictionary<int, string> GetRegistersNames() {
        return new() {
            { EsIndex, "ES" },
            { CsIndex, "CS" },
            { SsIndex, "SS" },
            { DsIndex, "DS" },
            { FsIndex, "FS" },
            { GsIndex, "GS" }
        };
    }
}
