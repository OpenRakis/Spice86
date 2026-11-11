namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.VM;

/// <summary>Registers and RAM captured when a run ends, to compare the emulated run with the generated run.</summary>
/// <param name="Registers">Registers and their values when the run ended.</param>
/// <param name="Ram">The first megabyte of RAM when the run ended.</param>
/// <param name="ExtendedRegions">Named byte regions beyond the first megabyte the settings enable:
/// the high memory area and <c>XmsRam</c> with A20 or XMS, every allocated EMS logical page with
/// EMS.</param>
internal sealed record AsmFinalState(IReadOnlyList<(string Name, uint Value)> Registers, byte[] Ram,
    IReadOnlyList<(string Name, byte[] Bytes)> ExtendedRegions) {
    /// <summary>Number of captured RAM bytes: the first megabyte.</summary>
    public const uint ComparedRamLength = 0x100000;

    /// <summary>Captures the registers and the RAM of a finished run.</summary>
    /// <param name="run">The finished run.</param>
    /// <param name="settings">Settings of the run, deciding which extended regions are compared.</param>
    /// <returns>The captured state.</returns>
    public static AsmFinalState Capture(AsmFixtureRun run, AsmRunSettings settings) {
        State state = run.Result.State;
        (string Name, uint Value)[] registers = [
            ("EAX", state.EAX),
            ("EBX", state.EBX),
            ("ECX", state.ECX),
            ("EDX", state.EDX),
            ("ESI", state.ESI),
            ("EDI", state.EDI),
            ("EBP", state.EBP),
            ("ESP", state.ESP),
            ("CS", state.CS),
            ("DS", state.DS),
            ("ES", state.ES),
            ("FS", state.FS),
            ("GS", state.GS),
            ("SS", state.SS),
            ("FLAGS", state.Flags.FlagRegister)
        ];
        // IP is not compared: the generated run ends at the override's hlt while the interpreted
        // run ends at the program's own, so CS:IP legitimately differs at the stopping point.
        byte[] ram = run.Result.Memory.ReadRam(ComparedRamLength, 0);
        List<(string Name, byte[] Bytes)> regions = [];
        if (settings.EnableA20Gate || settings.EnableXms) {
            regions.Add(("himem",
                run.Result.Memory.ReadRam((uint)(run.Machine.Memory.Length - (int)ComparedRamLength),
                    ComparedRamLength)));
            if (run.Machine.Dos.Xms is { } xms) {
                regions.Add(("xms", xms.XmsRam.GetSlice(0, (int)xms.XmsRam.Size).ToArray()));
            }
        }
        if (settings.EnableEms && run.Machine.Dos.Ems is { } ems) {
            foreach ((int handleId, EmmHandle handle) in ems.EmmHandles.OrderBy(pair => pair.Key)) {
                for (int pageIndex = 0; pageIndex < handle.LogicalPages.Count; pageIndex++) {
                    EmmPage page = handle.LogicalPages[pageIndex];
                    regions.Add(($"ems handle={handleId} page={pageIndex}",
                        page.GetSlice(0, (int)page.Size).ToArray()));
                }
            }
        }
        return new AsmFinalState(registers, ram, regions);
    }
}
