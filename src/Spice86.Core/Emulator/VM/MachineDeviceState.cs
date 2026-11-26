namespace Spice86.Core.Emulator.VM;

using Spice86.Core.Emulator.Devices.DirectMemoryAccess;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Input.Mouse;
using Spice86.Core.Emulator.Devices.Sound.Blaster;
using Spice86.Core.Emulator.Devices.Timer;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Ems;
using Spice86.Core.Emulator.InterruptHandlers.Dos.Xms;
using Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;
using Spice86.Core.Emulator.Memory;

/// <summary>
///     State of the devices of a <see cref="Machine" /> that implement
///     <see cref="Spice86.Core.Emulator.Devices.IStatefulDevice{TState}" />, as captured by
///     <see cref="Machine.CaptureDeviceState" />.
/// </summary>
/// <remarks>
///     A member is <see langword="null" /> when its device was left out of the capture, or when the machine does
///     not have it (XMS or EMS turned off). <see cref="Machine.RestoreDeviceState" /> restores the devices whose
///     member is set and leaves the others as they are. A device that gains state support later gets a member
///     here.
/// </remarks>
/// <param name="Pic">State of the interrupt controllers, or <see langword="null" />.</param>
/// <param name="Pit">State of the programmable interval timer, or <see langword="null" />.</param>
/// <param name="MouseDevice">State of the mouse device, or <see langword="null" />.</param>
/// <param name="MouseDriver">State of the INT 33h mouse driver, or <see langword="null" />.</param>
/// <param name="Dma">State of the DMA controllers, or <see langword="null" />.</param>
/// <param name="SoundBlaster">State of the Sound Blaster, or <see langword="null" />.</param>
/// <param name="A20Gate">State of the A20 line, or <see langword="null" />.</param>
/// <param name="Xms">State of the XMS driver, or <see langword="null" />.</param>
/// <param name="Ems">State of the EMS driver, or <see langword="null" />.</param>
public sealed record MachineDeviceState(
    DualPicState? Pic,
    PitTimerState? Pit,
    MouseDeviceState? MouseDevice,
    MouseDriverState? MouseDriver,
    DmaBusState? Dma,
    SoundBlasterState? SoundBlaster,
    A20GateState? A20Gate,
    XmsState? Xms,
    EmsState? Ems);
