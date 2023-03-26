namespace Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Interfaces;

/// <summary>
/// A wrapper class for the video card that implements the IMemoryDevice interface.
/// </summary>
public class VideoMemory : IMemoryDevice {
    private readonly IVideoCard _videoCard;
    private readonly uint _baseAddress;

    public VideoMemory(uint size, IVideoCard videoCard, uint baseAddress) {
        _videoCard = videoCard;
        _baseAddress = baseAddress;
        Size = size;
    }

    public byte[] GetStorage() {
        throw new NotSupportedException();
    }

    public uint Size {
        get;
    }
    public byte Read(uint address) {
        return _videoCard.GetVramByte(address - _baseAddress);
    }
    public void Write(uint address, byte value) {
        _videoCard.SetVramByte(address - _baseAddress, value);
    }

    public void WriteWord(uint address, ushort value) {
        throw new NotSupportedException();
    }

    public void WriteDWord(uint address, uint value) {
        throw new NotSupportedException();
    }

    public Span<byte> GetSpan(int address, int length) {
        throw new NotSupportedException();
    }
}