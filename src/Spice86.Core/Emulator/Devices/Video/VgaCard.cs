namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class VgaCard : IVideoCard {
    private readonly IGui? _gui;
    private readonly IVgaRenderer _renderer;


    private int _width;
    private int _height;
    private Resolution _currentResolution;

    public VgaCard(IGui? gui, IVgaRenderer renderer) {
        _gui = gui;
        _renderer = renderer;
        _currentResolution = renderer.CalculateResolution();
    }

    public void TickRetrace() {
        // throw new NotImplementedException();
    }

    public void UpdateScreen() {
        Resolution resolution = _renderer.CalculateResolution();
        if (resolution != _currentResolution) {
            _gui?.SetResolution(resolution.Width, resolution.Height);
            _currentResolution = resolution;
        }
        _gui?.UpdateScreen();
    }

    public void Render(uint address, object width, object height, IntPtr pixelsAddress) {
        throw new System.NotImplementedException();
    }

    public byte[] Render(uint address, IntPtr buffer, int size) {
        throw new System.NotImplementedException();
    }

    public void Render(Span<uint> buffer) {
        _renderer.Render(buffer);
    }
}