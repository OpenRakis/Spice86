namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;

public enum ModRmOffsetType {
    // 16bits
    BX_PLUS_SI,
    BX_PLUS_DI,
    BP_PLUS_SI,
    BP_PLUS_DI,
    SI,
    DI,
    OFFSET_FIELD_16,
    BP,
    BX,
    //32bits
    EAX,
    ECX,
    EDX,
    EBX,
    SIB,
    EBP,
    ESI,
    EDI
}