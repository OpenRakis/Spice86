namespace Spice86.Shared.Interfaces;

public interface IVideoCard {
    public void TickRetrace();
    public void UpdateScreen();
    void Render(uint address, object width, object height, nint pixelsAddress);
    byte[] Render(uint address, IntPtr buffer, int size);
    void Render(Span<uint> buffer);
}