namespace Spice86.Tests.AsmFixtures;

/// <summary>
/// Parts of a run's final state a scenario can leave out of the interpreted-vs-generated
/// comparison, with <see cref="AsmScenario.WithUncomparedParts"/>. RAM is never exempted this way:
/// only <see cref="AsmScenario.TimingDependentReason"/> leaves RAM out.
/// </summary>
[Flags]
internal enum FinalStateParts {
    None = 0,
    Console = 1,
    DriveFiles = 2,
    DeviceRegisters = 4,
    VideoMemory = 8
}
