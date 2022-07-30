namespace Spice86.Shared.Interfaces;

using Spice86.Shared;

using System.Collections.Generic;

public interface IGui {
    bool IsPaused { get; set; }

    public event EventHandler<EventArgs>? KeyUp;

    public event EventHandler<EventArgs>? KeyDown;

    void WaitOne();

    void RemoveBuffer(uint address);
    void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false);

    IDictionary<uint, IVideoBufferViewModel> VideoBuffersToDictionary { get; }
    int MouseX { get; set; }
    int MouseY { get; set; }

    void SetResolution(int videoWidth, int videoHeight, uint v);
    void Draw(byte[] ram, Rgb[] rgbs);

    bool IsLeftButtonClicked { get; }

    bool IsRightButtonClicked { get; }
    int Width { get; }
    int Height { get; }
}
