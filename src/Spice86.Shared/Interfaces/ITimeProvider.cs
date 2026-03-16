namespace Spice86.Shared.Interfaces;

using System;

/// <summary>
/// Provides current time.
/// </summary>
public interface ITimeProvider {
    /// <summary>
    /// Gets the current date and time.
    /// </summary>
    DateTime Now { get; }
}

/// <summary>
/// System implementation of <see cref="ITimeProvider"/>.
/// </summary>
public class SystemTimeProvider : ITimeProvider {
    /// <inheritdoc />
    public DateTime Now => DateTime.Now;
}
