namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.VM;
using Spice86.Shared.Emulator.Memory;

/// <summary>RAM captured when a run ends, to compare the emulated run with the generated run.</summary>
/// <param name="Ram">The first megabyte of RAM when the run ended.</param>
/// <param name="ExtendedRegions">Named byte regions beyond the first megabyte the settings enable:
/// the high memory area and <c>XmsRam</c> with A20 or XMS, every allocated EMS logical page with
/// EMS.</param>
internal sealed record AsmFinalState(byte[] Ram,
    IReadOnlyList<(string Name, byte[] Bytes)> ExtendedRegions) {
    /// <summary>Number of captured RAM bytes: the first megabyte.</summary>
    public const uint ComparedRamLength = 0x100000;

    /// <summary>The <c>int</c> instruction address and pre-push caller SS:SP of every software
    /// interrupt the run executed, recorded by <see cref="SoftwareInterruptRecorder"/>.</summary>
    public IReadOnlyList<(SegmentedAddress Address, SegmentedAddress StackPointer)> SoftwareInterrupts { get; init; } = [];

    /// <summary>The <c>call</c> instruction address and pre-call SS:SP of every call the run
    /// executed, recorded by <see cref="StackCallRecorder"/>.</summary>
    public IReadOnlyList<(SegmentedAddress Address, SegmentedAddress StackPointer)> Calls { get; init; } = [];

    /// <summary>The <c>push</c> instruction address and pre-push SS:SP of every push the run
    /// executed, recorded by <see cref="StackCallRecorder"/>.</summary>
    public IReadOnlyList<(SegmentedAddress Address, SegmentedAddress StackPointer)> Pushes { get; init; } = [];

    /// <summary>Every character written to the console during the run, recorded by
    /// <see cref="ConsoleOutputRecorder"/>.</summary>
    public string ConsoleOutput { get; init; } = "";

    /// <summary>The '/'-separated relative path and content of every file the run created or
    /// changed on its C: drive, recorded by <see cref="DriveFileRecorder"/>.</summary>
    public IReadOnlyList<(string Path, byte[] Content)> DriveFiles { get; init; } = [];

    /// <summary>Captures the RAM of a finished run. Final registers are not compared: a terminal
    /// <c>hlt</c> observes memory only, so the two runs may legitimately differ in registers.</summary>
    /// <param name="run">The finished run.</param>
    /// <param name="settings">Settings of the run, deciding which extended regions are compared.</param>
    /// <returns>The captured state.</returns>
    public static AsmFinalState Capture(AsmFixtureRun run, AsmRunSettings settings) {
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
        return new AsmFinalState(ram, regions) {
            SoftwareInterrupts = run.Result.Observations
                .Get<SoftwareInterruptRecorder>(AsmRunScope.SoftwareInterruptsObservation).Interrupts,
            Calls = run.Result.Observations
                .Get<StackCallRecorder>(AsmRunScope.StackCallsObservation).Calls,
            Pushes = run.Result.Observations
                .Get<StackCallRecorder>(AsmRunScope.StackCallsObservation).Pushes,
            ConsoleOutput = run.Result.Observations
                .Get<ConsoleOutputRecorder>(AsmRunScope.ConsoleOutputObservation).Text,
            DriveFiles = run.Result.Observations
                .Get<DriveFileRecorder>(AsmRunScope.DriveFilesObservation).WrittenFiles
        };
    }
}
