namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.Devices.Video;

/// <summary>
///     State of the devices of a <see cref="Machine" /> that implement
///     <see cref="Spice86.Core.Emulator.Devices.IStatefulDevice{TState}" />, as captured by
///     <see cref="Machine.CaptureDeviceState" />.
/// </summary>
/// <remarks>
///     A member is <see langword="null" /> when its device was left out of the capture.
///     <see cref="Machine.RestoreDeviceState" /> restores the devices whose member is set and leaves the others
///     as they are. A device that gains state support later gets a member here.
/// </remarks>
/// <param name="Pic">State of the interrupt controllers, or <see langword="null" />.</param>
/// <param name="Pit">State of the programmable interval timer, or <see langword="null" />.</param>
/// <param name="Vga">State of the VGA card, or <see langword="null" />.</param>
/// <param name="Dma">State of the DMA controllers, or <see langword="null" />.</param>
/// <param name="SoundBlaster">State of the Sound Blaster, or <see langword="null" />.</param>
public sealed record MachineDeviceState(DualPicState? Pic, PitTimerState? Pit, VgaState? Vga, DmaBusState? Dma,
    SoundBlasterState? SoundBlaster);
