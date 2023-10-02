namespace Spice86._3rdParty.Controls.HexView.Services;

using Spice86._3rdParty.Controls.HexView.Models;
using Spice86.Core.Emulator.Memory;

public class MemoryMappedLineReader : ILineReader {
    private readonly Stream _memory;

    public MemoryMappedLineReader(Stream memory) {
        _memory = memory;
    }

    public ReadOnlySpan<byte> GetLine(uint address, int width) {
        byte[] bytes = new byte[width];
        long offset = address * width;

        for (int i = 0; i < width; i++) {
            long position = offset + i;
            if (position > _memory.Length) {
                break;
            }
            int value = _memory.ReadByte();
            if (value == -1) {
                break;
            }
            bytes[i] = (byte)value;
        }
        return bytes;
    }
}