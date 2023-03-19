namespace Spice86.Shared.Interfaces;

public interface IVideoCard {
    public void TickRetrace();
    public void UpdateScreen();
    void SetVramByte(uint address, byte value);
    void Render(uint address, object width, object height, nint pixelsAddress);
    
    /// <summary>
    ///  Occurs when the emulated display mode has changed.
    /// </summary>
    public event EventHandler? VideoModeChanged;
    uint GetVramDWord(uint baseAddress);
    byte GetVramByte(uint baseAddress);
    ushort GetVramWord(uint baseAddress);
    void SetVramWord(uint baseAddress, ushort value);
    void SetVramDWord(uint baseAddress, uint value);
}