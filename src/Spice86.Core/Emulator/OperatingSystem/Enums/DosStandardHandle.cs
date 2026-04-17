namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Standard DOS file handles assigned at process startup (INT 21h).
/// These map to the first five entries in the per-process Job File Table.
/// </summary>
public enum DosStandardHandle : ushort {
    /// <summary>
    /// Standard input (CON device). Handle 0.
    /// </summary>
    Stdin = 0,

    /// <summary>
    /// Standard output (CON device). Handle 1.
    /// </summary>
    Stdout = 1,

    /// <summary>
    /// Standard error (CON device). Handle 2.
    /// </summary>
    Stderr = 2,

    /// <summary>
    /// Standard auxiliary device (AUX / COM1). Handle 3.
    /// </summary>
    StdAux = 3,

    /// <summary>
    /// Standard printer device (PRN / LPT1). Handle 4.
    /// </summary>
    StdPrn = 4,
}
