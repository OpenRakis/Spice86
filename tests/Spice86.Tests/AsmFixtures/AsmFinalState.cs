namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;

/// <summary>Registers and RAM captured when a run ends, to compare the emulated run with the generated run.</summary>
/// <param name="Registers">Registers and their values when the run ended.</param>
/// <param name="Ram">RAM bytes when the run ended.</param>
internal sealed record AsmFinalState(IReadOnlyList<(string Name, ushort Value)> Registers, byte[] Ram) {
    /// <summary>Number of captured RAM bytes: the first megabyte.</summary>
    public const uint ComparedRamLength = 0x100000;

    /// <summary>Captures the registers and the first megabyte of RAM of a finished run.</summary>
    /// <param name="result">Result of the finished run.</param>
    /// <returns>The captured state.</returns>
    public static AsmFinalState Capture(AsmFixtureResult result) {
        State state = result.State;
        (string Name, ushort Value)[] registers = [
            ("AX", state.AX),
            ("BX", state.BX),
            ("CX", state.CX),
            ("DX", state.DX),
            ("SI", state.SI),
            ("DI", state.DI),
            ("BP", state.BP),
            ("SP", state.SP),
            ("DS", state.DS),
            ("ES", state.ES),
            ("SS", state.SS),
            ("FS", state.FS),
            ("GS", state.GS),
            ("FLAGS", state.Flags.FlagRegister16)
        ];
        byte[] ram = result.Memory.ReadRam(ComparedRamLength, 0);
        return new AsmFinalState(registers, ram);
    }
}
