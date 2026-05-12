namespace Spice86.Core.Emulator.Devices.CdRom.Image;

/// <summary>Represents a single parsed directive from a CUE sheet file.</summary>
public sealed class CueEntry {
    /// <summary>Gets or sets the file name referenced by the FILE directive.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the track mode string (e.g. "MODE1/2048", "AUDIO").</summary>
    public string TrackMode { get; set; } = string.Empty;

    /// <summary>Gets or sets the track number (1–99).</summary>
    public int TrackNumber { get; set; }

    /// <summary>Gets or sets the index number within the track.</summary>
    public int IndexNumber { get; set; }

    /// <summary>Gets or sets the index position expressed as absolute CD frames (minutes*60*75 + seconds*75 + frames).</summary>
    public int IndexMsf { get; set; }

    /// <summary>Gets or sets the pregap length in frames.</summary>
    public int Pregap { get; set; }

    /// <summary>Gets or sets the postgap length in frames.</summary>
    public int Postgap { get; set; }
}
