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
    ///     The two DMA controllers and their eight channels (<see cref="Machine.DmaSystem" />).
    /// </summary>
    Dma = 256,

    /// <summary>
    ///     Every device that supports state capture.
    /// </summary>
    All = Pic | Pit | Dma
}
