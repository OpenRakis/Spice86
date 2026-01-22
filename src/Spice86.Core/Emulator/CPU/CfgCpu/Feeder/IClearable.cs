namespace Spice86.Core.Emulator.CPU.CfgCpu.Feeder;

/// <summary>
/// Interface for objects that can be cleared.
/// </summary>
public interface IClearable {
    /// <summary>
    /// Clears all internal state of the cache.
    /// </summary>
    void Clear();
}
