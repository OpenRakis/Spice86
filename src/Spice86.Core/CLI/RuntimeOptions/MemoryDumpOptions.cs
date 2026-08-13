namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Options used by memory dump exporters.
/// </summary>
/// <param name="DosRuntimeState">Shared mutable DOS runtime state used to decide callback replacement behavior.</param>
public sealed record class MemoryDumpOptions(DosRuntimeState DosRuntimeState);