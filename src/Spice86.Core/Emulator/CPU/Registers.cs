namespace Spice86.Core.Emulator.CPU;

/// <summary>
/// Represents the x86 registers.
/// </summary>
public class Registers : RegistersHolder {
    /// <summary>
    /// The index of the AX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int AxIndex = 0;

    /// <summary>
    /// The index of the BP register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int BpIndex = 5;
    
    /// <summary>
    /// The index of the BX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int BxIndex = 3;
    
    /// <summary>
    /// The index of the CX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int CxIndex = 1;

    /// <summary>
    /// The index of the DI register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int DiIndex = 7;

    /// <summary>
    /// The index of the DX register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int DxIndex = 2;
    
    /// <summary>
    /// The index of the SI register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int SiIndex = 6;
    
    /// <summary>
    /// The index of the SP register in the <see cref="GetRegistersNames"/> dictionary.
    /// </summary>
    public const int SpIndex = 4;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="Registers"/> class.
    /// </summary>
    public Registers() : base(GetRegistersNames()) {
    }

    private static Dictionary<int, string> GetRegistersNames() {
        return new() {
            { AxIndex, "AX" },
            { CxIndex, "CX" },
            { DxIndex, "DX" },
            { BxIndex, "BX" },
            { SpIndex, "SP" },
            { BpIndex, "BP" },
            { SiIndex, "SI" },
            { DiIndex, "DI" }
        };
    }
}