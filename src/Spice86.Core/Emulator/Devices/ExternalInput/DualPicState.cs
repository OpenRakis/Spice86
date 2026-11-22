namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Complete state of the two interrupt controllers, as captured by <see cref="DualPic.CaptureState" />.
/// </summary>
/// <param name="Primary">Controller of IRQ lines 0 to 7.</param>
/// <param name="Secondary">Controller of IRQ lines 8 to 15.</param>
/// <param name="IrqCheck">Value of <see cref="DualPic.IrqCheck" />: whether a request waits for the CPU.</param>
public readonly record struct DualPicState(PicControllerState Primary, PicControllerState Secondary, bool IrqCheck);
