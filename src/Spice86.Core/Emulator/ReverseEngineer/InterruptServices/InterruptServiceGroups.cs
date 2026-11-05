namespace Spice86.Core.Emulator.ReverseEngineer.InterruptServices;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Table between interrupt vectors that have typed service helpers and the name of the
/// <see cref="CSharpOverrideHelper"/> property holding the group, so generated code can
/// print <c>{propertyName}.{contractName}(...)</c> for a service.
/// </summary>
public static class InterruptServiceGroups {
    private static readonly IReadOnlyDictionary<byte, string> PropertyNames =
        new Dictionary<byte, string> {
            [0x10] = "Video",
            [0x11] = "Equipment",
            [0x12] = "MemorySize",
            [0x13] = "Disk",
            [0x14] = "Serial",
            [0x15] = "SystemBios",
            [0x16] = "Keyboard",
            [0x17] = "Printer",
            [0x1A] = "Clock",
            [0x1C] = "UserTimer",
            [0x21] = "Dos",
            [0x24] = "CriticalError",
            [0x28] = "DosIdle",
            [0x2A] = "DosNetwork",
            [0x2F] = "DosMultiplex",
            [0x33] = "Mouse",
            [0x67] = "Ems",
            [0x70] = "Rtc"
        };

    /// <summary>
    /// Returns the name of the <see cref="CSharpOverrideHelper"/> property that holds the
    /// typed service group of <paramref name="vector"/>.
    /// </summary>
    /// <param name="vector">The interrupt vector number.</param>
    /// <param name="propertyName">The property name when the vector has a service group.</param>
    /// <returns><c>false</c> when the vector has no typed service group.</returns>
    public static bool TryGetPropertyName(byte vector, [NotNullWhen(true)] out string? propertyName) {
        return PropertyNames.TryGetValue(vector, out propertyName);
    }
}
