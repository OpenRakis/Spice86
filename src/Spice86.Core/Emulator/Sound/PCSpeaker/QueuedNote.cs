namespace Spice86.Core.Emulator.Sound.PCSpeaker;

using System;

/// <summary>
/// Stores pitch and duration of a queued PC speaker note.
/// </summary>
internal readonly struct QueuedNote : IEquatable<QueuedNote> {
    /// <summary>
    /// Indicates a rest note.
    /// </summary>
    public static readonly QueuedNote Rest = default;

    /// <summary>
    /// Initializes a new instance of the QueuedNote struct.
    /// </summary>
    /// <param name="period">Length of a period in samples.</param>
    /// <param name="periodCount">Number of full periods in the note.</param>
    public QueuedNote(int period, int periodCount) {
        Period = period;
        PeriodCount = periodCount;
    }

    public static bool operator ==(QueuedNote noteA, QueuedNote noteB) => noteA.Equals(noteB);
    public static bool operator !=(QueuedNote noteA, QueuedNote noteB) => !noteA.Equals(noteB);

    /// <summary>
    /// Gets the length of half of a period in samples.
    /// </summary>
    public int Period { get; }
    /// <summary>
    /// Gets the number of full periods in the note.
    /// </summary>
    public int PeriodCount { get; }

    /// <summary>
    /// Returns a value indicating whether this instance is equal to another.
    /// </summary>
    /// <param name="other">Other instance to test for equality.</param>
    /// <returns>True if values are equal; otherwise false.</returns>
    public bool Equals(QueuedNote other) => Period == other.Period && PeriodCount == other.PeriodCount;
    /// <summary>
    /// Returns a value indicating whether this instance is equal to another.
    /// </summary>
    /// <param name="obj">Other instance to test for equality.</param>
    /// <returns>True if values are equal; otherwise false.</returns>
    public override bool Equals(object? obj) => obj is QueuedNote note && Equals(note);
    /// <summary>
    /// Returns a hash code for the instance.
    /// </summary>
    /// <returns>Hash code for the instance.</returns>
    public override int GetHashCode() => Period;
    /// <summary>
    /// Returns a string representation of the QueuedNote.
    /// </summary>
    /// <returns>String representation of the QueuedNote.</returns>
    public override string ToString() {
        if (this == Rest) {
            return "Rest";
        } else {
            return $"{Period}, {PeriodCount}";
        }
    }
}
