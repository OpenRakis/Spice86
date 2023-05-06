namespace Spice86.Core.Emulator.Devices.Video;

public interface IVgaRenderer {
    void Render(IntPtr bufferAddress, int size);
}