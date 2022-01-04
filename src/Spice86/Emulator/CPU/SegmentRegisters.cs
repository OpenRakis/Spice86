namespace Spice86.Emulator.Cpu;

using System.Collections.Generic;

public class SegmentRegisters : RegistersHolder
{
    public const int CsIndex = 1;

    public const int DsIndex = 3;

    public const int EsIndex = 0;

    public const int FsIndex = 4;

    public const int GsIndex = 5;

    public const int SsIndex = 2;

    public SegmentRegisters() : base(GetRegistersNames())
    {
    }

    private static Dictionary<int, string> GetRegistersNames()
    {
        Dictionary<int, string> res = new();
        res.Add(EsIndex, "ES");
        res.Add(CsIndex, "CS");
        res.Add(SsIndex, "SS");
        res.Add(DsIndex, "DS");
        res.Add(FsIndex, "FS");
        res.Add(GsIndex, "GS");
        return res;
    }
}