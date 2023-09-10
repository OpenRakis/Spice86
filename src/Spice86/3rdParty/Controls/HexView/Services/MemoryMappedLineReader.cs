namespace Spice86._3rdParty.Controls.HexView.Services;

using Spice86._3rdParty.Controls.HexView.Models;
using Spice86.Core.Emulator.Memory;

public class MemoryMappedLineReader : ILineReader {
    private readonly IMemory _memory;

    public MemoryMappedLineReader(IMemory memory) {
        _memory = memory;
    }

    public byte[] GetLine(long lineNumber, int width) {
        byte[] bytes = new byte[width];
        long offset = lineNumber * width;
        
        for (int i = 0; i < width; i++) {
            long position = offset + i;
            if (position > _memory.Length) {
                break;
            }
            bytes[i] = _memory.UInt8[position];
        }

        return bytes;
    }
}