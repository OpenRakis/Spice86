namespace Spice86.Core.Emulator.Memory.Mmu;

using Spice86.Core.Emulator.CPU;

/// <summary>
/// Creates the MMU for a CPU model: a plain real-mode MMU for pre-386 models (no descriptor cache
/// concept at all), or a descriptor-cache-based <see cref="CpuMmu"/> for the 386, which resolves both
/// real- and protected-mode accesses through the segment register's cached descriptor.
/// </summary>
public static class CpuMmuFactory {
    /// <summary>
    /// Creates the MMU configured for a CPU model.
    /// </summary>
    /// <param name="cpuModel">The configured CPU model.</param>
    /// <param name="state">The CPU state, needed to read segment registers and their descriptor caches.</param>
    /// <param name="ram">The raw memory device backing GDT/LDT reads.</param>
    public static IMmu Create(CpuModel cpuModel, State state, IMemoryDevice ram) {
        if (cpuModel != CpuModel.INTEL_80386) {
            return RealModeMmuFactory.FromCpuModel(cpuModel);
        }

        IMmu realModeMmu = RealModeMmuFactory.FromCpuModel(cpuModel);
        PagingUnit pagingUnit = new(state, ram);
        IMmu cachedSegmentMmu = new ProtectedModeMmu386(state, ram, pagingUnit);
        IMmu cpuMmu = new CpuMmu(state, realModeMmu, cachedSegmentMmu);
        return new PagingMmu(state, cpuMmu, pagingUnit);
    }
}
