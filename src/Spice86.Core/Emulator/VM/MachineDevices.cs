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
    ///     The state the DOS kernel (<see cref="Machine.Dos" />) keeps outside emulated memory.
    /// </summary>
    Dos = 64,

    /// <summary>
    ///     The Sound Blaster (<see cref="Machine.SoundBlaster" />): its DSP, mixer registers, DMA transfer and
    ///     scheduler events.
    /// </summary>
    SoundBlaster = 512,

    /// <summary>
    ///     Every device that supports state capture.
    /// </summary>
    All = Pic | Pit | Dos | SoundBlaster
}
