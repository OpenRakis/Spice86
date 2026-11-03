namespace Spice86.Core.Emulator.Platform;

/// <summary>Guest platform selected at startup.</summary>
public enum MachineType {
    /// <summary>The existing PC platform with VGA hardware.</summary>
    Pc,

    /// <summary>Tandy 1000 BIOS, shared video memory, and sound hardware.</summary>
    Tandy1000,

    /// <summary>Alias for the Tandy 1000 platform.</summary>
    Tandy = Tandy1000
}