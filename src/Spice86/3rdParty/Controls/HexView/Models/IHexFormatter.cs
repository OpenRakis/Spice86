namespace Spice86._3rdParty.Controls.HexView.Models;

using System.Text;

public interface IHexFormatter {
    long Lines { get; }
    int Width { get; set; }
    void AddLine(byte[] bytes, long lineNumber, StringBuilder sb, int toBase);
}