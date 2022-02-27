namespace Spice86.Emulator.Devices;

using Spice86.Emulator.VM;

using System.Threading.Tasks;

/// <summary>
/// Defines a virtual device for an emulated machine.
/// </summary>
public interface IVirtualDevice {
    /// <summary>
    /// Invoked when the emulator enters a paused state.
    /// </summary>
    void Pause() { }
    /// <summary>
    /// Invoked when the emulator resumes from a paused state.
    /// </summary>
    void Resume() { }
    /// <summary>
    /// Invoked when the virtual device has been added to a VirtualMachine.
    /// </summary>
    /// <param name="vm">VirtualMachine which owns the device.</param>
    void DeviceRegistered(Machine vm) {
    }
}
