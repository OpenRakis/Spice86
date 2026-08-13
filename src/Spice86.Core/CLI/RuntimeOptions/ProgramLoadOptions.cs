namespace Spice86.Core.CLI.RuntimeOptions;

/// <summary>
/// Options consumed by program bootstrap and DOS loaders.
/// </summary>
/// <param name="Exe">Host path of the entry executable or image to load.</param>
/// <param name="ExeArgs">Raw command tail passed to the loaded program.</param>
/// <param name="ExpectedChecksumValue">Expected SHA-256 checksum bytes used for validation.</param>
/// <param name="CDrive">Host path mapped to DOS C: root.</param>
/// <param name="DosRuntimeState">Shared mutable DOS runtime state.</param>
public sealed record class ProgramLoadOptions(
    string Exe,
    string? ExeArgs,
    byte[] ExpectedChecksumValue,
    string? CDrive,
    DosRuntimeState DosRuntimeState);