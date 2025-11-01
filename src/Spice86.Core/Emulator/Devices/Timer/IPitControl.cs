// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Provides control and status queries for Programmable Interval Timer (PIT) channel 2.
/// </summary>
public interface IPitControl {
    /// <summary>
    ///     Sets the logical level of the gate input for PIT channel 2.
    /// </summary>
    /// <param name="input">True when the gate is high (open), false when it is low (closed).</param>
    void SetGate2(bool input);

    /// <summary>
    ///     Gets a value indicating whether the output signal of PIT channel 2 is high.
    /// </summary>
    /// <returns>True if the output is high; otherwise, false.</returns>
    bool IsChannel2OutputHigh();
}