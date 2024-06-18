namespace Spice86.Core.Emulator.CPU.Registers;

using System.Collections.Frozen;

/// <summary>
/// Represents the x86 registers.
/// </summary>
public class GeneralRegisters : RegistersHolder {
    
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralRegisters"/> class.
    /// </summary>
    public GeneralRegisters() : base(GetRegistersNames()) {
    }

    private static readonly FrozenDictionary<uint, string> _registersNames = new Dictionary<uint, string>()
        {
        
            { (uint)RegisterIndex.AxIndex, "AX" },
            { (uint)RegisterIndex.CxIndex, "CX" },
            { (uint)RegisterIndex.DxIndex, "DX" },
            { (uint)RegisterIndex.BxIndex, "BX" },
            { (uint)RegisterIndex.SpIndex, "SP" },
            { (uint)RegisterIndex.BpIndex, "BP" },
            { (uint)RegisterIndex.SiIndex, "SI" },
            { (uint)RegisterIndex.DiIndex, "DI" }
        }.ToFrozenDictionary();

    private static FrozenDictionary<uint, string> GetRegistersNames() => _registersNames;
}