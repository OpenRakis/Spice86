namespace Spice86.Shared.Emulator.Storage.FileSystem.BootSector;

/// <summary>
/// A single issue reported by <see cref="FatBootSectorValidator"/>.
/// Issues are pure observations: validators never mutate the BPB.
/// </summary>
/// <param name="Severity">Severity of the issue.</param>
/// <param name="Field">Name of the BPB field that is inconsistent.</param>
/// <param name="Message">Human readable description, including the offending values.</param>
public sealed record BpbValidationIssue(BpbValidationSeverity Severity, string Field, string Message);
