namespace Spice86._3rdParty.Controls.HexView.Services;

using Spice86._3rdParty.Controls.HexView.Models;
using Spice86.Shared.Utils;

using System;
using System.Text;

public class HexFormatter : IHexFormatter {
    private readonly long _length;
    private long _lines;
    private int _width;
    private readonly int _offsetPadding;

    public HexFormatter(long length) {
        _length = length;
        _width = 8;
        _lines = (long)Math.Ceiling((decimal)_length / _width);
        _offsetPadding = _length.ToString("X").Length;
    }

    public long Lines => _lines;

    public int Width {
        get => _width;
        set {
            _width = value;
            _lines = (long)Math.Ceiling((decimal)_length / _width);
        }
    }

    public void AddLine(ReadOnlySpan<byte> bytes, uint startAddress, StringBuilder sb, int toBase) {
        if (toBase != 2 && toBase != 8 && toBase != 10 && toBase != 16) {
            throw new ArgumentException("Invalid base");
        }

        int width = _width;
        long offset = startAddress * width;

        sb.Append("0x").Append(offset.ToString($"X{_offsetPadding}")).Append(": ");

        int toBasePadding = toBase switch {
            2 => 8,
            8 => 3,
            10 => 3,
            16 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
        };

        char paddingChar = toBase switch {
            2 => '0',
            8 => ' ',
            10 => ' ',
            16 => '0',
            _ => throw new ArgumentOutOfRangeException(nameof(toBase), toBase, null)
        };

        for (int j = 0; j < width; j++) {
            long position = offset + j;

            bool isSplit = j > 0 && j % 8 == 0;
            if (isSplit) {
                sb.Append("| ");
            }

            if (position < _length) {
                if (toBase == 16) {
                    string value = $"{bytes[j]:X2}";
                    sb.Append(value);
                } else {
                    string value = Convert.ToString(bytes[j], toBase).PadLeft(toBasePadding, paddingChar);
                    sb.Append(value);
                }
            } else {
                string value = new string(' ', toBasePadding);
                sb.Append(value);
            }

            sb.Append(' ');
        }

        sb.Append(" | ");

        for (int j = 0; j < width; j++) {
            char c = (char)bytes[j];

            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        sb.Append(" | ");
        sb.Append(ConvertUtils.ToSegmentedAddressRepresentation(MemoryUtils.ToSegment((uint)offset), 0));
    }
}