namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

public class VgaCard : IVideoCard {
    private readonly IGui? _gui;
    private readonly IVgaRenderer _renderer;
    private readonly Bios _bios;
    private int _currentModeId = 0;

    public static readonly Dictionary<byte, (int width, int height)> Resolutions = new() {
        [0x00] = (360, 400),
        [0x01] = (360, 400),
        [0x02] = (720, 400),
        [0x03] = (720, 400),
        [0x04] = (320, 200),
        [0x05] = (320, 200),
        [0x06] = (640, 200),
        [0x07] = (720, 400),
        [0x0D] = (320, 200),
        [0x0E] = (640, 200),
        [0x0F] = (640, 350),
        [0x10] = (640, 350),
        [0x11] = (640, 480),
        [0x12] = (640, 480),
        [0x13] = (320, 200),
        [0x6A] = (800, 600)
    };

    private int _width;
    private int _height;

    public VgaCard(IGui? gui, IVgaRenderer renderer, Bios bios) {
        _gui = gui;
        _renderer = renderer;
        _bios = bios;
    }

    public void TickRetrace() {
        // throw new NotImplementedException();
    }

    public void UpdateScreen() {
        byte biosVideoMode = _bios.VideoMode;
        if (biosVideoMode != _currentModeId) {
            (int width, int height) = Resolutions[biosVideoMode];
            if (width != _width || height != _height) {
                _gui?.SetResolution(width, height);
                _width = width;
                _height = height;
            }
            _currentModeId = biosVideoMode;
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