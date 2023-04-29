namespace Spice86.Shared.Interfaces;

public interface IVideoCard {
    public void TickRetrace();
    public void UpdateScreen();
    byte GetVramByte(uint address);
    void SetVramByte(uint address, byte value);
    void Render(uint address, object width, object height, nint pixelsAddress);
    void Render(uint address, IntPtr buffer, int size);
}