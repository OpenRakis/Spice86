namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.CPU;

internal sealed record CpuStateSnapshot {
    public required uint EAX { get; init; }

    public required uint EBX { get; init; }

    public required uint ECX { get; init; }

    public required uint EDX { get; init; }

    public required uint ESI { get; init; }

    public required uint EDI { get; init; }

    public required uint ESP { get; init; }

    public required uint EBP { get; init; }

    public required ushort CS { get; init; }

    public required ushort DS { get; init; }

    public required ushort ES { get; init; }

    public required ushort FS { get; init; }

    public required ushort GS { get; init; }

    public required ushort SS { get; init; }

    public required ushort IP { get; init; }

    public required bool CarryFlag { get; init; }

    public required bool ParityFlag { get; init; }

    public required bool AuxiliaryFlag { get; init; }

    public required bool ZeroFlag { get; init; }

    public required bool SignFlag { get; init; }

    public required bool DirectionFlag { get; init; }

    public required bool OverflowFlag { get; init; }

    public required bool InterruptFlag { get; init; }

    public required long Cycles { get; init; }

    public static CpuStateSnapshot FromState(State state) {
        return new CpuStateSnapshot {
            EAX = state.EAX, EBX = state.EBX, ECX = state.ECX, EDX = state.EDX,
            ESI = state.ESI, EDI = state.EDI, ESP = state.ESP, EBP = state.EBP,
            CS = state.CS, DS = state.DS, ES = state.ES,
            FS = state.FS, GS = state.GS, SS = state.SS,
            IP = state.IP,
            CarryFlag = state.CarryFlag, ParityFlag = state.ParityFlag,
            AuxiliaryFlag = state.AuxiliaryFlag, ZeroFlag = state.ZeroFlag,
            SignFlag = state.SignFlag, DirectionFlag = state.DirectionFlag,
            OverflowFlag = state.OverflowFlag, InterruptFlag = state.InterruptFlag,
            Cycles = state.Cycles
        };
    }
}
