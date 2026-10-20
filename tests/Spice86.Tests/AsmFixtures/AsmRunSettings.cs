namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Devices.Sound;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;

/// <summary>Run settings of an ASM fixture: the bin to run, the cycle failsafe and the machine configuration.</summary>
internal sealed record AsmRunSettings {
    /// <summary>Machine configuration that leaves the fresh machine untouched.</summary>
    public static readonly Action<Machine> NoMachineConfiguration = _ => { };

    /// <summary>No mounted folder drives.</summary>
    public static readonly IReadOnlyDictionary<char, string> NoMountedFolderDrives = new Dictionary<char, string>();

    /// <summary>No mounted disk images.</summary>
    public static readonly IReadOnlyDictionary<char, string> NoMountedImages = new Dictionary<char, string>();

    /// <summary>Bin name or rooted path of the program to run, as <see cref="Spice86Creator"/> accepts it.</summary>
    public required string BinName { get; init; }

    /// <summary>
    /// Stable logical name of the scenario, independent of host paths. Empty means the bin file name
    /// without extension is the identity, which keeps the CPU fixture keys unchanged.
    /// </summary>
    public string ScenarioName { get; init; } = "";

    /// <summary>
    /// Logical input or configuration variant of the scenario, e.g. a selector arm. Empty for the
    /// default variant. Tells two prepared runs of the same scenario apart without comparing paths
    /// or delegates.
    /// </summary>
    public string VariantName { get; init; } = "";

    /// <summary>Cycle failsafe: reaching it fails the test. Not a bound on a healthy run.</summary>
    public long MaxCycles { get; init; } = 100000;

    /// <summary>Whether the PIT runs during the program.</summary>
    public bool EnablePit { get; init; }

    /// <summary>Whether the A20 gate is open.</summary>
    public bool EnableA20Gate { get; init; }

    /// <summary>Whether the BIOS interrupt vectors are installed.</summary>
    public bool InstallInterruptVectors { get; init; }

    /// <summary>Whether an unhandled I/O port access fails the run.</summary>
    public bool FailOnUnhandledPort { get; init; }

    /// <summary>Whether speculative CFG exploration runs during discovery.</summary>
    public bool EnableSpeculativeCfgExploration { get; init; } = true;

    /// <summary>Scheduler mode used by this fixture's video device.</summary>
    public RenderingMode RenderingMode { get; init; } = RenderingMode.Async;

    /// <summary>Segment for installed BIOS callbacks, separate from BIOS-image fixture code when needed.</summary>
    public ushort AsmHandlersSegment { get; init; } = 0xF000;

    /// <summary>Golden suffix for relocated callbacks; unchanged when callbacks are not installed.</summary>
    public string HandlerSegmentGoldenSuffix {
        get {
            if (!InstallInterruptVectors || AsmHandlersSegment == 0xF000) {
                return "";
            }
            return $".handlers{AsmHandlersSegment:X4}";
        }
    }

    /// <summary>Host folder mounted as drive C:, or null for no C: drive.</summary>
    public string? CDrive { get; init; }

    /// <summary>Whether XMS is enabled.</summary>
    public bool EnableXms { get; init; }

    /// <summary>Whether EMS is enabled.</summary>
    public bool EnableEms { get; init; }

    /// <summary>Command line arguments passed to the program, or null for none.</summary>
    public string? ExeArgs { get; init; }

    /// <summary>Instruction time scale in instructions per second, or null for the deterministic default.</summary>
    public long? InstructionTimeScale { get; init; }

    /// <summary>Sound Blaster model the machine exposes.</summary>
    public SbType SbType { get; init; } = SbType.None;

    /// <summary>OPL synthesis mode the machine exposes.</summary>
    public OplMode OplMode { get; init; } = OplMode.None;

    /// <summary>Host folders mounted as extra DOS drives, keyed by drive letter, before the program runs.</summary>
    public IReadOnlyDictionary<char, string> MountedFolderDrives { get; init; } = NoMountedFolderDrives;

    /// <summary>Host ISO images mounted as CD-ROM drives, keyed by drive letter, before the program runs.
    /// The runner unmounts them again once the program ended, whether it succeeded or failed.</summary>
    public IReadOnlyDictionary<char, string> MountedCdRomImages { get; init; } = NoMountedImages;

    /// <summary>Host raw FAT images mounted as floppy drives, keyed by drive letter, before the program runs.
    /// The runner unmounts them again once the program ended, whether it succeeded or failed.</summary>
    public IReadOnlyDictionary<char, string> MountedFloppyImages { get; init; } = NoMountedImages;

    /// <summary>Keyboard events injected through the headless GUI at a CPU cycle count, each fired exactly
    /// once when the run reaches that cycle count.</summary>
    public IReadOnlyList<HeadlessKeyInjection> HeadlessKeyInjections { get; init; } = [];

    /// <summary>Key codes preloaded into the BIOS keyboard buffer before the program runs.</summary>
    public IReadOnlyList<ushort> PreloadedKeyCodes { get; init; } = [];

    /// <summary>Whether DOS upper memory blocks are enabled.</summary>
    public bool EnableUmb { get; init; } = true;

    /// <summary>Runs on the fresh machine before the program runs. Never null.</summary>
    public Action<Machine> ConfigureMachine { get; init; } = NoMachineConfiguration;

    /// <summary>
    /// Stable identity of the scenario: the declared scenario name, or the bin file name without
    /// extension when no name is declared. Never contains a host path, so two runs of the same
    /// scenario prepared in different directories keep the same identity.
    /// </summary>
    public string ScenarioIdentity => ScenarioName.Length > 0 ? ScenarioName : Path.GetFileNameWithoutExtension(BinName);

    /// <summary>Creates the creator that runs the program with the given JIT mode.</summary>
    /// <param name="jitMode">JIT mode to run in.</param>
    /// <returns>The creator, ready to build the emulator.</returns>
    public Spice86Creator CreateEmulatedCreator(JitMode jitMode) {
        return new Spice86Creator(this, jitMode, null);
    }

    /// <summary>Creates the creator that runs the program in interpreted-only mode with an override supplier.</summary>
    /// <param name="overrideSupplier">Supplier of the code overrides to install.</param>
    /// <returns>The creator, ready to build the emulator.</returns>
    public Spice86Creator CreateOverrideCreator(IOverrideSupplier overrideSupplier) {
        return new Spice86Creator(this, JitMode.InterpretedOnly, overrideSupplier);
    }
}
