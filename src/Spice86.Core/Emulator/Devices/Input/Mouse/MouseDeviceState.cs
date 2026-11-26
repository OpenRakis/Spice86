namespace Spice86.Core.Emulator.Devices.Input.Mouse;

/// <summary>
///     Complete state of the mouse device, as captured by <see cref="Mouse.CaptureState" />.
/// </summary>
/// <remarks>
///     The ranges, the press and release counters and the user routine belong to the mouse driver and are part
///     of <see cref="Spice86.Core.Emulator.InterruptHandlers.Input.Mouse.MouseDriverState" />. The state of the
///     IRQ 12 line is part of the interrupt controller's state.
/// </remarks>
/// <param name="MouseXRelative">Horizontal position, from 0 (left) to 1 (right).</param>
/// <param name="MouseYRelative">Vertical position, from 0 (top) to 1 (bottom).</param>
/// <param name="IsLeftButtonDown">Whether the left button is down.</param>
/// <param name="IsRightButtonDown">Whether the right button is down.</param>
/// <param name="IsMiddleButtonDown">Whether the middle button is down.</param>
/// <param name="DeltaX">Horizontal motion accumulated since the motion counters were last reset.</param>
/// <param name="DeltaY">Vertical motion accumulated since the motion counters were last reset.</param>
/// <param name="LastTrigger">Events of the last sample delivered with IRQ 12.</param>
/// <param name="PendingTrigger">Events seen since the last delivered sample and not delivered yet.</param>
/// <param name="PreviousMouseXRelative">Horizontal position at the last host event, used to detect motion.</param>
/// <param name="PreviousMouseYRelative">Vertical position at the last host event, used to detect motion.</param>
/// <param name="PreviousIsLeftButtonDown">Left button at the last host event, used to detect a change.</param>
/// <param name="PreviousIsRightButtonDown">Right button at the last host event, used to detect a change.</param>
/// <param name="PreviousIsMiddleButtonDown">Middle button at the last host event, used to detect a change.</param>
/// <param name="SampleRate">Samples per second.</param>
/// <param name="DoubleSpeedThreshold">Double speed threshold in mickeys per second.</param>
/// <param name="HorizontalMickeysPerPixel">Horizontal mickeys per 8 pixels.</param>
/// <param name="VerticalMickeysPerPixel">Vertical mickeys per 8 pixels.</param>
/// <param name="CapturedAtMs">Elapsed milliseconds on the emulated clock at capture.</param>
/// <param name="LastSampleTimeMs">
///     Elapsed milliseconds on the emulated clock when the last sample was delivered, or
///     <see langword="null" /> when none was.
/// </param>
/// <param name="PendingSampleDelayMs">
///     Milliseconds from capture to the sample event queued in the scheduler, which delivers the pending
///     events. Negative when the event was overdue. <see langword="null" /> when no event was queued.
/// </param>
public readonly record struct MouseDeviceState(
    double MouseXRelative,
    double MouseYRelative,
    bool IsLeftButtonDown,
    bool IsRightButtonDown,
    bool IsMiddleButtonDown,
    double DeltaX,
    double DeltaY,
    MouseEventMask LastTrigger,
    MouseEventMask PendingTrigger,
    double PreviousMouseXRelative,
    double PreviousMouseYRelative,
    bool PreviousIsLeftButtonDown,
    bool PreviousIsRightButtonDown,
    bool PreviousIsMiddleButtonDown,
    int SampleRate,
    int DoubleSpeedThreshold,
    int HorizontalMickeysPerPixel,
    int VerticalMickeysPerPixel,
    double CapturedAtMs,
    double? LastSampleTimeMs,
    double? PendingSampleDelayMs);
