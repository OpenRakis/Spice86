namespace Spice86.Core.Emulator.Devices.CdRom;

/// <summary>Tracks the physical media and door state of a CD-ROM drive.</summary>
public sealed class CdRomMediaState {
    /// <summary>Gets or sets a value indicating whether the drive door is locked.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Gets or sets a value indicating whether the drive door is open.</summary>
    public bool IsDoorOpen { get; set; }

    private bool _mediaChanged;

    /// <summary>
    /// Gets a value indicating whether the media has changed since the last call to
    /// <see cref="ReadAndClearMediaChanged"/>.
    /// </summary>
    public bool MediaChanged => _mediaChanged;

    /// <summary>
    /// Initialises a new <see cref="CdRomMediaState"/> with the media-changed flag set,
    /// reflecting that newly inserted media is considered changed by default.
    /// </summary>
    public CdRomMediaState() {
        _mediaChanged = true;
    }

    /// <summary>Sets the media-changed flag to indicate that the disc has been replaced or removed.</summary>
    public void NotifyMediaChanged() {
        _mediaChanged = true;
    }

    /// <summary>
    /// Returns the current value of the media-changed flag and then clears it.
    /// </summary>
    /// <returns><see langword="true"/> if the media changed since the last call; otherwise <see langword="false"/>.</returns>
    public bool ReadAndClearMediaChanged() {
        bool current = _mediaChanged;
        _mediaChanged = false;
        return current;
    }
}
