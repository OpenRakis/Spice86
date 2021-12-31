namespace Ix86.Emulator.Cpu;
using System.Collections.Generic;

public class Registers : RegistersHolder
{
    public const int AxIndex = 0;
    public const int CxIndex = 1;
    public const int DxIndex = 2;
    public const int BxIndex = 3;
    public const int SpIndex = 4;
    public const int BpIndex = 5;
    public const int SiIndex = 6;
    public const int DiIndex = 7;

    public Registers() : base(GetRegistersNames())
    {

    }

    private static Dictionary<int, string> GetRegistersNames()
    {
        Dictionary<int, string> res = new();
        res.Add(AxIndex, "AX");
        res.Add(CxIndex, "CX");
        res.Add(DxIndex, "DX");
        res.Add(BxIndex, "BX");
        res.Add(SpIndex, "SP");
        res.Add(BpIndex, "BP");
        res.Add(SiIndex, "SI");
        res.Add(DiIndex, "DI");
        return res;
    }
}