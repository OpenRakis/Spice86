namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Shared.Interfaces;

public class VgaCard : IVideoCard {
    private readonly IGui? _gui;
    private readonly IVgaRenderer _renderer;
    private bool _initialized;

    public VgaCard(IGui? gui, IVgaRenderer renderer) {
        _gui = gui;
        _renderer = renderer;
    }

    public void TickRetrace() {
        // throw new NotImplementedException();
    }

    public void UpdateScreen() {
        if (!_initialized) {
            _gui?.SetResolution(640, 480,0xA000);
            _initialized = true;
        }
        _gui?.UpdateScreen();
    }

    public void Render(uint address, object width, object height, IntPtr pixelsAddress) {
        throw new System.NotImplementedException();
    }

    public void Render(uint address, IntPtr buffer, int size) {
        _renderer.Render(buffer, size);
    }
}