namespace Spice86.Core.Emulator.InterruptHandlers.Input.Mouse;

using System.Collections.Immutable;

/// <summary>
///     Complete state of the INT 33h mouse driver, as captured by <see cref="MouseDriver.CaptureState" />.
/// </summary>
/// <remarks>
///     The position, the buttons, the motion counters and the mickey ratios belong to the mouse device and are
///     part of <see cref="Spice86.Core.Emulator.Devices.Input.Mouse.MouseDeviceState" />. The address the driver
///     stub calls is in emulated memory.
/// </remarks>
/// <param name="ButtonPresses">Press counters, read by INT 33h function 05h.</param>
/// <param name="ButtonReleases">Release counters, read by INT 33h function 06h.</param>
/// <param name="CursorHiddenCounter">Cursor visibility counter. The cursor shows when it is 0.</param>
/// <param name="UserCallback">User routine and its event mask, set by INT 33h functions 0Ch and 14h.</param>
/// <param name="ScreenHeight">Height in pixels of the video mode the driver was last reset for.</param>
/// <param name="MinX">Lower bound of the horizontal position.</param>
/// <param name="MaxX">Upper bound of the horizontal position.</param>
/// <param name="MinY">Lower bound of the vertical position.</param>
/// <param name="MaxY">Upper bound of the vertical position.</param>
/// <param name="IsUserRoutineRunning">Whether the user routine is running.</param>
/// <param name="SavedRegisters">
///     Registers saved before the running user routine was called, or <see langword="null" /> when none are.
/// </param>
/// <param name="PassesInProgress">
///     One entry per driver pass in progress, innermost last: whether that pass called the user routine.
/// </param>
public sealed record MouseDriverState(
    MouseButtonCountersState ButtonPresses,
    MouseButtonCountersState ButtonReleases,
    int CursorHiddenCounter,
    MouseUserCallback UserCallback,
    int ScreenHeight,
    int MinX,
    int MaxX,
    int MinY,
    int MaxY,
    bool IsUserRoutineRunning,
    MouseRegisters? SavedRegisters,
    ImmutableArray<bool> PassesInProgress);
