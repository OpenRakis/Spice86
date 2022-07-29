namespace Spice86.Tests;

using Spice86.Emulator.Devices.Video;
using Spice86.UI.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal class FakeGui : IGui {
    public bool IsPaused { get; set; }
    public IDictionary<uint, IVideoBufferViewModel> VideoBuffersAsDictionary { get; } = new Dictionary<uint, IVideoBufferViewModel>();
    public int MouseX { get; set; }
    public int MouseY { get; set; }
    public bool IsLeftButtonClicked { get; }
    public bool IsRightButtonClicked { get; }
    public int Width { get; }
    public int Height { get; }

    public event EventHandler<EventArgs>? KeyUp;
    public event EventHandler<EventArgs>? KeyDown;

    public void AddBuffer(uint address, double scale, int bufferWidth, int bufferHeight, bool isPrimaryDisplay = false) {
        throw new NotImplementedException();
    }

    public void Draw(byte[] ram, Rgb[] rgbs) {
        throw new NotImplementedException();
    }

    public void RemoveBuffer(uint address) {
        throw new NotImplementedException();
    }

    public void SetResolution(int videoWidth, int videoHeight, uint v) {
        throw new NotImplementedException();
    }

    public void WaitOne() {
        throw new NotImplementedException();
    }
}
