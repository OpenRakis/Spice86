namespace Spice86.Core.Emulator.CPU;

/// <summary>
/// The addressing/execution mode the CPU is currently operating in, derived from
/// <see cref="Registers.ControlRegisters.ProtectionEnable"/> and the EFLAGS VM bit.
/// </summary>
public enum CpuMode {
    /// <summary>Real mode: 16-bit segmented addressing, no protection.</summary>
    Real,

    /// <summary>Protected mode: descriptor-table-based segmentation with privilege checks.</summary>
    Protected,

    /// <summary>Virtual-8086 mode: real-mode-style execution inside a protected-mode task.</summary>
    Virtual8086
}
