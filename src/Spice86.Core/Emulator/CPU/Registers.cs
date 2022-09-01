namespace Spice86.Core.Emulator.CPU;

using System.Collections.Generic;

public class Registers : RegistersHolder {
    public const int AxIndex = 0;

    public const int BpIndex = 5;

    public const int BxIndex = 3;

    public const int CxIndex = 1;

    public const int DiIndex = 7;

    public const int DxIndex = 2;

    public const int SiIndex = 6;

    public const int SpIndex = 4;

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