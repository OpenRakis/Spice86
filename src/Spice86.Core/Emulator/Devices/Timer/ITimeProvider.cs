// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Provides deterministic access to the current UTC time and delay primitives.
/// </summary>
public interface ITimeProvider {
    /// <summary>
    ///     Gets the current coordinated universal time.
    /// </summary>
    DateTime UtcNow { get; }
}