namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Creates real-mode MMU instances from configured CPU models.
/// </summary>
public static class RealModeMmuFactory {
    /// <summary>
    /// Creates a real-mode MMU for a CPU model.
    /// </summary>
    /// <param name="cpuModel">The configured CPU model.</param>
    /// <returns>The MMU configured for the CPU model.</returns>
    public static IMmu FromCpuModel(CpuModel cpuModel) {
        return cpuModel switch {
            CpuModel.ZET_86 => new RealModeMmu8086(),
            CpuModel.INTEL_8086 => new RealModeMmu8086(),
            _ => new RealModeMmu386()
        };
    }
}