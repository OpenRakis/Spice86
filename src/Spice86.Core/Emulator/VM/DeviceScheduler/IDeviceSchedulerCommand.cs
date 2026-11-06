namespace Spice86.Core.Emulator.VM.DeviceScheduler;

/// <summary>Device work that must execute on its scheduler's owning thread.</summary>
public interface IDeviceSchedulerCommand {
    /// <summary>Executes the device operation on the scheduler thread.</summary>
    void Execute();
}