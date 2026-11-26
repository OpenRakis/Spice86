namespace Spice86.Core.Emulator.VM;

/// <summary>
///     Devices whose state <see cref="Machine.CaptureDeviceState" /> can capture.
/// </summary>
[Flags]
public enum MachineDevices {
    /// <summary>
    ///     No device.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The two interrupt controllers (<see cref="Machine.DualPic" />).
    /// </summary>
    Pic = 1,

    /// <summary>
    ///     The programmable interval timer (<see cref="Machine.Timer" />) and its scheduler event.
    /// </summary>
    Pit = 2,

    /// <summary>
    ///     The mouse device (<see cref="Machine.MouseDevice" />) with its sample event, and the INT 33h mouse driver
    ///     (<see cref="Machine.MouseDriver" />).
    /// </summary>
    Mouse = 128,

    /// <summary>
    ///     Every device that supports state capture.
    /// </summary>
    All = Pic | Pit | Mouse
}
