namespace Spice86.Core.Emulator.CPU.Registers;

using System.Collections.Frozen;

/// <summary>
/// Represents the x86 registers.
/// </summary>
public class GeneralRegisters : RegistersHolder {
    /// <summary>
    /// The index of the AX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint AxIndex = 0;

    /// <summary>
    /// The index of the BP register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint BpIndex = 5;
    
    /// <summary>
    /// The index of the BX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint BxIndex = 3;
    
    /// <summary>
    /// The index of the CX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint CxIndex = 1;

    /// <summary>
    /// The index of the DI register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint DiIndex = 7;

    /// <summary>
    /// The index of the DX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint DxIndex = 2;
    
    /// <summary>
    /// The index of the SI register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint SiIndex = 6;
    
    /// <summary>
    /// The index of the SP register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const uint SpIndex = 4;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralRegisters"/> class.
    /// </summary>
    public GeneralRegisters() : base(GetRegistersNames()) {
    }

    private static readonly FrozenDictionary<uint, string> _registersNames = new Dictionary<uint, string>()
        {
        
            { AxIndex, "AX" },
            { CxIndex, "CX" },
            { DxIndex, "DX" },
            { BxIndex, "BX" },
            { SpIndex, "SP" },
            { BpIndex, "BP" },
            { SiIndex, "SI" },
            { DiIndex, "DI" }
        }.ToFrozenDictionary();

    private static FrozenDictionary<uint, string> GetRegistersNames() => _registersNames;
}