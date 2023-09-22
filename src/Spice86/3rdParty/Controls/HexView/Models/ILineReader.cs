namespace Spice86._3rdParty.Controls.HexView.Models;

public interface ILineReader {
    ReadOnlySpan<byte> GetLine(uint address, int length);
}