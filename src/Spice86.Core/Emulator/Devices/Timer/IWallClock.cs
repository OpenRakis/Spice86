// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Provides deterministic access to the current UTC time.
/// </summary>
public interface IWallClock {
    /// <summary>
    ///     Gets the current coordinated universal time.
    /// </summary>
    DateTime UtcNow { get; }
}