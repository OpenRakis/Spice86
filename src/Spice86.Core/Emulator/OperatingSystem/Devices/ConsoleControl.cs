namespace Spice86.Core.Emulator.OperatingSystem.Devices;

/// <summary>
/// Provides control over the internal <see cref="ConsoleDevice"/> behavior, when standard output/input isn't redirected.
/// </summary>
public sealed class ConsoleControl {
    public bool InternalOutput { get; set; }

    public bool Echo { get; set; }

    public bool DirectOutput { get; set; }
}
