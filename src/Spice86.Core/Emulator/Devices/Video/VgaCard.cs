namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Shared.Interfaces;

/// <summary>
/// Thin interface between renderer and gui.
/// </summary>
public class VgaCard : IVideoCard {
    private readonly IGui? _gui;
    private readonly IVgaRenderer _renderer;
    private Resolution _currentResolution;

    /// <summary>
    ///    Create a new VGA card.
    /// </summary>
    /// <param name="gui"></param>
    /// <param name="renderer"></param>
    public VgaCard(IGui? gui, IVgaRenderer renderer) {
        _gui = gui;
        _renderer = renderer;
        _currentResolution = renderer.CalculateResolution();
    }

    /// <inheritdoc />
    public void UpdateScreen() {
        Resolution resolution = _renderer.CalculateResolution();
        if (resolution != _currentResolution) {
            _gui?.SetResolution(resolution.Width, resolution.Height);
            _currentResolution = resolution;
        }
        _gui?.UpdateScreen();
    }

    /// <inheritdoc />
    public void Render(Span<uint> buffer) {
        _renderer.Render(buffer);
    }
}