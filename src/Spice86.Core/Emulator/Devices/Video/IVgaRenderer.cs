namespace Spice86.Core.Emulator.Devices.Video;

public interface IVgaRenderer {
    void Render(IntPtr bufferAddress, int size);
    void Render(Span<uint> buffer);
    Resolution CalculateResolution();
}