// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Receives PIT channel 2 updates so that the PC speaker bridge can react to reloads and mode changes.
/// </summary>
public interface IPitSpeaker {
    /// <summary>
    ///     Sets the counter value and mode for PIT channel 2.
    /// </summary>
    /// <param name="count">The current reload counter value.</param>
    /// <param name="mode">The active PIT operational mode.</param>
    void SetCounter(int count, PitMode mode);

    /// <summary>
    ///     Informs the speaker bridge about a control word update for PIT channel 2.
    /// </summary>
    /// <param name="mode">The operational mode defined by the control word.</param>
    void SetPitControl(PitMode mode);
}