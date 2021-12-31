namespace Ix86.Emulator.Cpu;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class SegmentRegisters : RegistersHolder
{
    public static readonly int ES_INDEX = 0;
    public static readonly int CS_INDEX = 1;
    public static readonly int SS_INDEX = 2;
    public static readonly int DS_INDEX = 3;
    public static readonly int FS_INDEX = 4;
    public static readonly int GS_INDEX = 5;
    public SegmentRegisters() : base(GetRegistersNames())
    {
    }

    private static Dictionary<int, string> GetRegistersNames()
    {
        Dictionary<int, string> res = new();
        res.Add(ES_INDEX, "ES");
        res.Add(CS_INDEX, "CS");
        res.Add(SS_INDEX, "SS");
        res.Add(DS_INDEX, "DS");
        res.Add(FS_INDEX, "FS");
        res.Add(GS_INDEX, "GS");
        return res;
    }
}
