namespace Spice86.Core.Emulator.CPU;

using System.Collections.Generic;

public class SegmentRegisters : RegistersHolder {
    public const int CsIndex = 1;

    public const int DsIndex = 3;

    public const int EsIndex = 0;

    public const int FsIndex = 4;

    public const int GsIndex = 5;

    public const int SsIndex = 2;

    public SegmentRegisters() : base(GetRegistersNames()) {
    }

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