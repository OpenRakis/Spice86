namespace Spice86.Shared.Interfaces;

/// <summary>
/// Interface representing a video card for a computer system.
/// </summary>
public interface IVideoCard {
    /// <summary>
    /// Method to be called during the vertical retrace period of the display.
    /// </summary>
    void TickRetrace();

    /// <summary>
    /// Method to update the screen with the contents of the video memory.
    /// </summary>
    void UpdateScreen();

    /// <summary>
    /// Method to retrieve a byte from video memory at a specific address.
    /// </summary>
    /// <param name="address">The address of the byte to retrieve.</param>
    /// <returns>The byte value at the specified address in video memory.</returns>
    byte GetVramByte(uint address);

    /// <summary>
    /// Method to set a byte in video memory at a specific address.
    /// </summary>
    /// <param name="address">The address of the byte to set.</param>
    /// <param name="value">The byte value to set at the specified address in video memory.</param>
    void SetVramByte(uint address, byte value);

    /// <summary>
    /// Method to render graphics onto the screen from video memory.
    /// </summary>
    /// <param name="address">The starting address in video memory to render from.</param>
    /// <param name="width">The width of the graphics to render.</param>
    /// <param name="height">The height of the graphics to render.</param>
    /// <param name="pixelsAddress">The address in memory where the pixel data is stored.</param>
    void Render(uint address, object width, object height, nint pixelsAddress);
    byte[] Render(uint address, IntPtr buffer, int size);
    void Render(Span<uint> buffer);
}