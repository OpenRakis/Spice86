namespace Spice86.Aeon.Emulator.Video.Modes; 
/// <summary>
/// Implements functionality for chained 8-bit 256-color VGA modes.
/// </summary>
public sealed class Vga256 : VideoMode
{
    /// <summary>
    /// Pointer to the video memory of the VGA card.
    /// </summary>
    private readonly unsafe byte* videoRam;

    /// <summary>
    /// Constructs a new instance of <see cref="Vga256"/> with the specified width, height, video memory, and video card.
    /// </summary>
    /// <param name="width">The width of the screen in pixels.</param>
    /// <param name="height">The height of the screen in pixels.</param>
    /// <param name="video">The VGA card that this mode will use.</param>
    public Vga256(int width, int height, IAeonVgaCard video) : base(width, height, 8, false, 8, VideoModeType.Graphics, video)
    {
        unsafe
        {
            videoRam = (byte*)video.VideoRam.ToPointer();
        }
    }

    /// <inheritdoc/>
    public override int MouseWidth => PixelWidth * 2;

    /// <inheritdoc/>
    public override byte GetVramByte(uint offset)
    {
        unsafe
        {
            return videoRam[offset];
        }
    }

    /// <inheritdoc/>
    public override void SetVramByte(uint offset, byte value)
    {
        unsafe
        {
            videoRam[offset] = value;
        }
    }

    /// <inheritdoc/>
    internal override ushort GetVramWord(uint offset)
    {
        unsafe
        {
            return *(ushort*)(videoRam + offset);
        }
    }

    /// <inheritdoc/>
    internal override void SetVramWord(uint offset, ushort value)
    {
        unsafe
        {
            *(ushort*)(videoRam + offset) = value;
        }
    }

    /// <inheritdoc/>
    internal override uint GetVramDWord(uint offset)
    {
        unsafe
        {
            return *(uint*)(videoRam + offset);
        }
    }

    /// <inheritdoc/>
    internal override void SetVramDWord(uint offset, uint value)
    {
        unsafe
        {
            *(uint*)(videoRam + offset) = value;
        }
    }

    /// <inheritdoc/>
    internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
    {
        unsafe
        {
            int stride = Stride;
            int startPos = (y * stride * 8) + x * 8;
            byte[] font = Font;

            for (int row = 0; row < 8; row++)
            {
                uint value = font[index * 8 + row];
                int pos = startPos + (row * stride);

                for (int column = 0; column < 8; column++)
                    videoRam[pos + column] = (value & (0x80 >> column)) != 0 ? foreground : background;
            }
        }
    }
}