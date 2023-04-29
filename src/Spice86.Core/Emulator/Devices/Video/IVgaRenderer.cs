namespace Spice86.Core.Emulator.Devices.Video;

internal interface IVgaRenderer {
    void Render(IntPtr bufferAddress, int size);
}