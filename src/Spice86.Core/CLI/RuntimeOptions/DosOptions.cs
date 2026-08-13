namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Options consumed by DOS subsystem composition.
/// </summary>
/// <param name="CDrive">Host path mapped to DOS C: root.</param>
/// <param name="Exe">Host path of the entry executable or image.</param>
/// <param name="ProgramEntryPointSegment">Program load segment in emulated memory.</param>
/// <param name="Xms">Whether XMS services are enabled.</param>
/// <param name="Ems">Whether EMS services are enabled.</param>
/// <param name="DosRuntimeState">Shared mutable DOS runtime state.</param>
public sealed record class DosOptions(
    string? CDrive,
    string Exe,
    ushort ProgramEntryPointSegment,
    bool? Xms,
    bool? Ems,
    DosRuntimeState DosRuntimeState);