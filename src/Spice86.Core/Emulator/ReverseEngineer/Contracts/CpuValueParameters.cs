namespace Spice86.Core.Emulator.ReverseEngineer.Contracts;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;


/// <summary>
/// Decomposes a <see cref="CpuValues"/> set into the parameter list it describes:
/// one parameter per register field and per flag, in a fixed order (<c>ax</c>,
/// <c>eax</c>, <c>al</c>, <c>ah</c>, <c>ds</c>, <c>carryFlag</c>, ...,
/// <c>directionFlag</c>). Used both for generated method signatures and for
/// interrupt service calls.
/// </summary>
public static class CpuValueParameters {
    /// <summary>The parameters of <paramref name="values"/> in describe order. SP, ESP,
    /// SS and CS are never produced: no parameter shape covers them.</summary>
    public static List<TypedParameter> Decompose(CpuValues values) {
        List<TypedParameter> parameters = [];
        GeneralRegister(parameters, values, CpuValues.Eax, CpuValues.Ax, CpuValues.Al,
            CpuValues.Ah, "eax", "EAX", "ax", "AX", "al", "AL", "ah", "AH");
        GeneralRegister(parameters, values, CpuValues.Ecx, CpuValues.Cx, CpuValues.Cl,
            CpuValues.Ch, "ecx", "ECX", "cx", "CX", "cl", "CL", "ch", "CH");
        GeneralRegister(parameters, values, CpuValues.Edx, CpuValues.Dx, CpuValues.Dl,
            CpuValues.Dh, "edx", "EDX", "dx", "DX", "dl", "DL", "dh", "DH");
        GeneralRegister(parameters, values, CpuValues.Ebx, CpuValues.Bx, CpuValues.Bl,
            CpuValues.Bh, "ebx", "EBX", "bx", "BX", "bl", "BL", "bh", "BH");
        WordRegister(parameters, values, CpuValues.Ebp, CpuValues.Bp, "ebp", "EBP", "bp", "BP");
        WordRegister(parameters, values, CpuValues.Esi, CpuValues.Si, "esi", "ESI", "si", "SI");
        WordRegister(parameters, values, CpuValues.Edi, CpuValues.Di, "edi", "EDI", "di", "DI");
        SegmentRegister(parameters, values, CpuValues.Es, "es", "ES");
        SegmentRegister(parameters, values, CpuValues.Ds, "ds", "DS");
        SegmentRegister(parameters, values, CpuValues.Fs, "fs", "FS");
        SegmentRegister(parameters, values, CpuValues.Gs, "gs", "GS");
        StatusFlag(parameters, values, CpuValues.Carry, "carryFlag", "CarryFlag");
        StatusFlag(parameters, values, CpuValues.Parity, "parityFlag", "ParityFlag");
        StatusFlag(parameters, values, CpuValues.Auxiliary, "auxiliaryFlag", "AuxiliaryFlag");
        StatusFlag(parameters, values, CpuValues.Zero, "zeroFlag", "ZeroFlag");
        StatusFlag(parameters, values, CpuValues.Sign, "signFlag", "SignFlag");
        StatusFlag(parameters, values, CpuValues.Overflow, "overflowFlag", "OverflowFlag");
        StatusFlag(parameters, values, CpuValues.Direction, "directionFlag", "DirectionFlag");
        return parameters;
    }

    private static void GeneralRegister(List<TypedParameter> parameters, CpuValues values,
        CpuValues mask32, CpuValues mask16, CpuValues maskLow, CpuValues maskHigh,
        string name32, string register32, string name16, string register16,
        string nameLow, string registerLow, string nameHigh, string registerHigh) {
        CpuValues bits = values & mask32;
        if (bits == CpuValues.None) {
            return;
        }
        if (bits == mask16) {
            parameters.Add(new TypedParameter(name16, "ushort", register16, mask16));
        } else if (bits == maskLow) {
            parameters.Add(new TypedParameter(nameLow, "byte", registerLow, maskLow));
        } else if (bits == maskHigh) {
            parameters.Add(new TypedParameter(nameHigh, "byte", registerHigh, maskHigh));
        } else {
            parameters.Add(new TypedParameter(name32, "uint", register32, mask32));
        }
    }

    private static void WordRegister(List<TypedParameter> parameters, CpuValues values,
        CpuValues mask32, CpuValues mask16, string name32, string register32,
        string name16, string register16) {
        CpuValues bits = values & mask32;
        if (bits == CpuValues.None) {
            return;
        }
        if (bits == mask16) {
            parameters.Add(new TypedParameter(name16, "ushort", register16, mask16));
            return;
        }
        parameters.Add(new TypedParameter(name32, "uint", register32, mask32));
    }

    private static void SegmentRegister(List<TypedParameter> parameters, CpuValues values,
        CpuValues mask, string name, string register) {
        if ((values & mask) != CpuValues.None) {
            parameters.Add(new TypedParameter(name, "ushort", register, mask));
        }
    }

    private static void StatusFlag(List<TypedParameter> parameters, CpuValues values,
        CpuValues mask, string name, string register) {
        if ((values & mask) != CpuValues.None) {
            parameters.Add(new TypedParameter(name, "bool", register, mask));
        }
    }
}
