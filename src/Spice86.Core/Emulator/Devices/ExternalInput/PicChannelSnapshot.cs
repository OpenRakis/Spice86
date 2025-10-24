namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Represents a read-only view of a PIC channel's register state at a specific instant.
/// </summary>
/// <remarks>
///     Instances are produced by <see cref="DualPic.GetChannelSnapshot" /> and mirror the register layout maintained by
///     the controller.
/// </remarks>
/// <param name="InterruptRequestRegister">Latched, unserviced IRQ requests.</param>
/// <param name="InterruptMaskRegister">Mask bits suppressing IRQ delivery.</param>
/// <param name="InterruptMaskRegisterInverted">Cached inversion of the mask register.</param>
/// <param name="InServiceRegister">IRQs currently in service.</param>
/// <param name="InServiceRegisterInverted">Cached inversion of the in-service register.</param>
/// <param name="ActiveIrqLine">Active IRQ index or 8 when idle.</param>
/// <param name="IsSpecialMaskModeEnabled">Indicates a special mask mode state.</param>
/// <param name="IsAutoEndOfInterruptEnabled">Indicates auto end-of-interrupt configuration.</param>
/// <param name="ShouldRotateOnAutoEoi">Indicates whether rotate-on-auto-EOI is requested.</param>
/// <param name="IsSingleModeConfigured">Indicates whether the controller operates in single mode.</param>
/// <param name="InterruptVectorBase">Base interrupt vector applied to IRQ numbers.</param>
public readonly record struct PicChannelSnapshot(
    byte InterruptRequestRegister,
    byte InterruptMaskRegister,
    byte InterruptMaskRegisterInverted,
    byte InServiceRegister,
    byte InServiceRegisterInverted,
    byte ActiveIrqLine,
    bool IsSpecialMaskModeEnabled,
    bool IsAutoEndOfInterruptEnabled,
    bool ShouldRotateOnAutoEoi,
    bool IsSingleModeConfigured,
    byte InterruptVectorBase
);