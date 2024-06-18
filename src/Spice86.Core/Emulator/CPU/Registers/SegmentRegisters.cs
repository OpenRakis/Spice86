namespace Spice86.Core.Emulator.CPU.Registers;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// A class that represents the segment registers of a CPU. <br/>
/// The segment registers are registers that store segment selectors, which are used to access different parts of memory.
/// </summary>
public class SegmentRegisters : RegistersHolder {
    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentRegisters"/> class with the segment register names.
    /// </summary>
    public SegmentRegisters() : base(GetRegistersNames()) {
    }

    private static readonly FrozenDictionary<uint, string> _registersNames = new Dictionary<uint, string>() {
            { (uint)SegmentRegisterIndex.EsIndex, "ES" },
            { (uint)SegmentRegisterIndex.CsIndex, "CS" },
            { (uint)SegmentRegisterIndex.SsIndex, "SS" },
            { (uint)SegmentRegisterIndex.DsIndex, "DS" },
            { (uint)SegmentRegisterIndex.FsIndex, "FS" },
            { (uint)SegmentRegisterIndex.GsIndex, "GS" }
        }.ToFrozenDictionary();

    /// <summary>
    /// Gets a dictionary that maps the segment register index to its name.
    /// </summary>
    /// <returns>A dictionary that maps the segment register index to its name.</returns>
    private static FrozenDictionary<uint, string> GetRegistersNames() => _registersNames;
}
