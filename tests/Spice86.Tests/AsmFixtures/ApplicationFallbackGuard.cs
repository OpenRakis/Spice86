namespace Spice86.Tests.AsmFixtures;

using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using Xunit.Sdk;

/// <summary>
/// Rejects a generated override run in which the interpreter executed anything that is not positively
/// identified emulator-provided code. The decision is per instruction and default-deny: an address is
/// accepted only when the run's own <see cref="EmulatorCodeInventory"/> identifies the bytes the
/// interpreter was about to run as the asm glue of an emulator callback registered on that machine.
/// No memory range, segment, fixture name or override registration is an exemption, so a CPU test image
/// loaded in the BIOS address range, an EXEC child and a DOS program are all application code, and code
/// the emulator no longer owns loses its exemption as soon as the memory content changes.
/// </summary>
internal static class ApplicationFallbackGuard {
    /// <summary>How many offending addresses the rejection message lists before summarising the rest.</summary>
    private const int MaxReportedAddresses = 8;

    /// <summary>Fails when the interpreter executed anything that is not emulator-provided code during a
    /// generated override run.</summary>
    /// <param name="observations">Nodes the interpreter executed, in execution order.</param>
    /// <param name="inventory">Emulator-provided code of that run, captured before the program started.</param>
    /// <param name="scenarioIdentity">Identity of the scenario whose run is guarded.</param>
    /// <exception cref="XunitException">The interpreter executed application code.</exception>
    public static void AssertNoInterpreterExecutedApplicationCode(
        IReadOnlyList<ObservedExecution> observations, EmulatorCodeInventory inventory, string scenarioIdentity) {
        List<ObservedExecution> offenders =
            [.. observations.Where(observation => !inventory.IsEmulatorProvided(observation.Address, observation.InstructionBytes))];
        if (offenders.Count == 0) {
            return;
        }
        IEnumerable<string> reported = offenders.Take(MaxReportedAddresses).Select(Describe);
        string addressList = string.Join(", ", reported);
        if (offenders.Count > MaxReportedAddresses) {
            addressList += $" and {offenders.Count - MaxReportedAddresses} more";
        }
        throw new XunitException(
            $"scenario '{scenarioIdentity}': the interpreter executed application code at {addressList}, " +
            "so the generated override run fell back to the interpreter for application code");
    }

    private static string Describe(ObservedExecution observation) {
        uint linearAddress = MemoryUtils.ToPhysicalAddress(observation.Address.Segment, observation.Address.Offset);
        string bytes = Convert.ToHexString(observation.InstructionBytes);
        return
            $"{observation.Address.Segment:X4}:{observation.Address.Offset:X4} (linear 0x{linearAddress:X}, " +
            $"{observation.NodeKind}, bytes {bytes})";
    }
}
