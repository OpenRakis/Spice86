namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Projects command-line configuration into runtime option records consumed by specific subsystems.
/// </summary>
public static class RuntimeOptionsMapper {
    /// <summary>
    /// Creates shared mutable DOS runtime state from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <returns>The shared DOS runtime state.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
    public static DosRuntimeState CreateDosRuntimeState(Configuration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        return new DosRuntimeState(configuration.InitializeDOS);
    }

    /// <summary>
    /// Builds execution policy options from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <returns>Execution policy options for runtime services.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
    public static ExecutionPolicyOptions ToExecutionPolicyOptions(Configuration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        GdbServerOptions gdbServerOptions = ToGdbServerOptions(configuration);
        return new ExecutionPolicyOptions(
            configuration.Debug,
            configuration.StopAfterCycles,
            gdbServerOptions);
    }

    /// <summary>
    /// Builds GDB server options from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <returns>GDB server runtime options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
    public static GdbServerOptions ToGdbServerOptions(Configuration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        return new GdbServerOptions(configuration.GdbPort);
    }

    /// <summary>
    /// Builds program-load options from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <param name="dosRuntimeState">Shared mutable DOS runtime state.</param>
    /// <returns>Program-load runtime options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is <c>null</c>.</exception>
    public static ProgramLoadOptions ToProgramLoadOptions(Configuration configuration, DosRuntimeState dosRuntimeState) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(dosRuntimeState);

        return new ProgramLoadOptions(
            configuration.Exe,
            configuration.ExeArgs,
            configuration.ExpectedChecksumValue,
            configuration.CDrive,
            dosRuntimeState);
    }

    /// <summary>
    /// Builds DOS subsystem options from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <param name="dosRuntimeState">Shared mutable DOS runtime state.</param>
    /// <returns>DOS subsystem runtime options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is <c>null</c>.</exception>
    public static DosOptions ToDosOptions(Configuration configuration, DosRuntimeState dosRuntimeState) {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(dosRuntimeState);

        return new DosOptions(
            configuration.CDrive,
            configuration.Exe,
            configuration.ProgramEntryPointSegment,
            configuration.Xms,
            configuration.Ems,
            dosRuntimeState);
    }

    /// <summary>
    /// Builds memory dump options from a shared DOS runtime state object.
    /// </summary>
    /// <param name="dosRuntimeState">Shared mutable DOS runtime state.</param>
    /// <returns>Memory dump options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dosRuntimeState"/> is <c>null</c>.</exception>
    public static MemoryDumpOptions ToMemoryDumpOptions(DosRuntimeState dosRuntimeState) {
        ArgumentNullException.ThrowIfNull(dosRuntimeState);

        return new MemoryDumpOptions(dosRuntimeState);
    }

    /// <summary>
    /// Builds audio runtime options from command-line configuration.
    /// </summary>
    /// <param name="configuration">The parsed command-line configuration.</param>
    /// <returns>Audio runtime options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is <c>null</c>.</exception>
    public static AudioRuntimeOptions ToAudioRuntimeOptions(Configuration configuration) {
        ArgumentNullException.ThrowIfNull(configuration);

        return new AudioRuntimeOptions(
            configuration.AudioEngine,
            configuration.Mt32RomsPath,
            configuration.OplMode,
            configuration.SbBase,
            configuration.SbMixer is true,
            configuration.SbIrq,
            configuration.SbDma,
            configuration.SbHdma,
            configuration.SbType);
    }
}