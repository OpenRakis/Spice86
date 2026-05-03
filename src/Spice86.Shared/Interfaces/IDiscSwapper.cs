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
}
