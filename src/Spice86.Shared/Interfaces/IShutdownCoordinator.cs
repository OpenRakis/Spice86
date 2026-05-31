namespace Spice86.Shared.Interfaces;

/// <summary>
/// Coordinates shutdown work after the emulator loop stops.
/// </summary>
public interface IShutdownCoordinator {
    /// <summary>
    /// Performs shutdown work after emulation completes.
    /// </summary>
    void Shutdown();
}