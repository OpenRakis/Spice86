namespace Spice86._3rdParty.Controls.HexView.Models;

public interface ILineReader {
    byte[] GetLine(long lineNumber, int width);
}