// SPDX-License-Identifier: GPL-3.0-or-later

namespace Spice86.Core.Emulator.Devices.Timer;

/// <summary>
///     Wall-clock-backed time provider.
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider {
    private SystemTimeProvider() {
    }

    /// <summary>
    ///     Gets the singleton <see cref="SystemTimeProvider" /> instance backed by the system clock.
    /// </summary>
    public static SystemTimeProvider Instance { get; } = new();

    /// <summary>
    ///     Gets the current UTC timestamp as reported by <see cref="DateTime.UtcNow" />.
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}