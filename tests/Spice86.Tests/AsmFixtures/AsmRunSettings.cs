namespace Spice86.Tests.AsmFixtures;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.VM;

/// <summary>Run settings of an ASM fixture: the bin to run, the cycle failsafe and the machine configuration.</summary>
internal sealed record AsmRunSettings {
    /// <summary>Machine configuration that leaves the fresh machine untouched.</summary>
    public static readonly Action<Machine> NoMachineConfiguration = _ => { };

    /// <summary>Bin name or rooted path of the program to run, as <see cref="Spice86Creator"/> accepts it.</summary>
    public required string BinName { get; init; }

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

    /// <summary>Runs on the fresh machine before the program runs. Never null.</summary>
    public Action<Machine> ConfigureMachine { get; init; } = NoMachineConfiguration;

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
