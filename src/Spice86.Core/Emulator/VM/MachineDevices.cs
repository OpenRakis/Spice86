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
    ///     The A20 line (<see cref="Spice86.Core.Emulator.Memory.IMemory.A20Gate" />).
    /// </summary>
    A20Gate = 4,

    /// <summary>
    ///     The XMS driver (<see cref="Spice86.Core.Emulator.OperatingSystem.Dos.Xms" />), with the content of
    ///     extended memory.
    /// </summary>
    Xms = 8,

    /// <summary>
    ///     The EMS driver (<see cref="Spice86.Core.Emulator.OperatingSystem.Dos.Ems" />), with the content of its
    ///     pages.
    /// </summary>
    Ems = 16,

    /// <summary>
    ///     The VGA card (<see cref="Machine.VgaCard" />): registers, DAC, video memory planes and latches.
    /// </summary>
    Vga = 32,

    /// <summary>
    ///     The state the DOS kernel (<see cref="Machine.Dos" />) keeps outside emulated memory.
    /// </summary>
    Dos = 64,

    /// <summary>
    ///     The two DMA controllers and their eight channels (<see cref="Machine.DmaSystem" />).
    /// </summary>
    Dma = 256,

    /// <summary>
    ///     The Sound Blaster (<see cref="Machine.SoundBlaster" />): its DSP, mixer registers, DMA transfer and
    ///     scheduler events.
    /// </summary>
    SoundBlaster = 512,

    /// <summary>
    ///     Every device that supports state capture.
    /// </summary>
    All = Pic | Pit | A20Gate | Xms | Ems | Vga | Dos | Dma | SoundBlaster
}
