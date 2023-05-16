namespace Spice86.Aeon.Emulator.Video.Modes; 

/// <summary>
/// Provides functionality for the 320x200 4-color CGA video mode.
/// </summary>
public sealed class CgaMode4 : VideoMode
{
    private const uint BaseAddress = 0x18000;
    private readonly unsafe byte* videoRam;

    /// <summary>
    /// Initializes a new instance of the <see cref="CgaMode4"/> class.
    /// </summary>
    /// <param name="video">The video card that will use this mode.</param>
    public CgaMode4(IAeonVgaCard video) : base(320, 200, 2, false, 8, VideoModeType.Graphics, video)
    {
        unsafe
        {
            videoRam = (byte*)video.VideoRam.ToPointer();
        }
    }

    /// <inheritdoc/>
    public override int Stride => 80;

    /// <inheritdoc/>
    public override byte GetVramByte(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return videoRam[offset];
        }
    }

    /// <inheritdoc/>
    public override void SetVramByte(uint offset, byte value)
    {
        offset -= BaseAddress;
        unsafe
        {
            videoRam[offset] = value;
        }
    }

    internal override ushort GetVramWord(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return *(ushort*)(videoRam + offset);
        }
    }

    internal override void SetVramWord(uint offset, ushort value)
    {
        offset -= BaseAddress;
        unsafe
        {
            *(ushort*)(videoRam + offset) = value;
        }
    }

    internal override uint GetVramDWord(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return *(uint*)(videoRam + offset);
        }
    }

    internal override void SetVramDWord(uint offset, uint value)
    {
        offset -= BaseAddress;
        unsafe
        {
            *(uint*)(videoRam + offset) = value;
        }
    }

    internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
    {
        throw new NotImplementedException("WriteCharacter in CGA.");
    }
}