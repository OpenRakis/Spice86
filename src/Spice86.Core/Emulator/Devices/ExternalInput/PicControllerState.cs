namespace Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
///     Complete state of one Intel 8259 controller, as captured by <see cref="DualPic.CaptureState" />.
/// </summary>
/// <remarks>
///     <see cref="PicSnapshot" /> is a view for inspection and leaves out the initialization sequence, the
///     register selected for command port reads and the priority rotation. This record holds those as well, so
///     a controller can be restored from it. The inverted register copies the controller caches are derived
///     from the registers on restore and are not stored.
/// </remarks>
/// <param name="InterruptRequestRegister">Latched requests not yet in service, one bit per line.</param>
/// <param name="InterruptMaskRegister">Mask bits. A set bit suppresses the line.</param>
/// <param name="InServiceRegister">Lines in service, one bit per line.</param>
/// <param name="ActiveIrqLine">Line that has priority now (0 to 7), or 8 when none is in service.</param>
/// <param name="LowestPriorityIrq">Line with the lowest priority, which sets the rotation.</param>
/// <param name="InterruptVectorBase">Vector of line 0, from ICW2.</param>
/// <param name="IsSpecialMaskModeEnabled">Whether special mask mode is on.</param>
/// <param name="IsAutoEndOfInterruptEnabled">Whether auto end-of-interrupt is on.</param>
/// <param name="ShouldRotateOnAutoEoi">Whether priorities rotate on an auto end-of-interrupt.</param>
/// <param name="IsSingleModeConfigured">Whether ICW1 configured the controller without a cascade.</param>
/// <param name="IsIssrRequested">
///     Whether a command port read returns the in-service register. Otherwise it returns the request register.
/// </param>
/// <param name="CurrentIcwIndex">Position in the initialization sequence. 0 outside of one.</param>
/// <param name="RemainingInitializationWords">Number of initialization words ICW1 announced.</param>
public readonly record struct PicControllerState(
    byte InterruptRequestRegister,
    byte InterruptMaskRegister,
    byte InServiceRegister,
    byte ActiveIrqLine,
    byte LowestPriorityIrq,
    byte InterruptVectorBase,
    bool IsSpecialMaskModeEnabled,
    bool IsAutoEndOfInterruptEnabled,
    bool ShouldRotateOnAutoEoi,
    bool IsSingleModeConfigured,
    bool IsIssrRequested,
    int CurrentIcwIndex,
    int RemainingInitializationWords);
