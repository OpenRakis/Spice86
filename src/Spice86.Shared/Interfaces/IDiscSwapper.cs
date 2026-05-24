namespace Spice86.Shared.Interfaces;

/// <summary>
/// Allows the UI or keyboard hotkeys to advance every mounted drive with multiple disc images
/// to the next image in its list (cycling). This is the Ctrl-F4 "swap disc" feature.
/// </summary>
public interface IDiscSwapper {
    /// <summary>
    /// Advances every mounted drive that has more than one image registered to the next image in its list,
    /// cycling back to the first image after the last one has been played.
    /// </summary>
    void SwapDiscImages();

    /// <summary>
    /// Advances the specified drive to the image at the given zero-based index.
    /// Has no effect if the drive is not found or the index is out of range.
    /// </summary>
    /// <param name="driveLetter">The drive letter to target.</param>
    /// <param name="imageIndex">The zero-based index of the image to switch to.</param>
    void SwapToImageIndex(char driveLetter, int imageIndex);
}
