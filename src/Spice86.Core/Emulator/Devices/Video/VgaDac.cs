
namespace Spice86.Core.Emulator.Devices.Video;

using Serilog;

using Spice86.Core.Emulator.Devices.Video;

using Spice86.Core.Emulator.VM;
using Spice86.Shared;

/// <summary>
/// VGA Digital Analog Converter Implementation.
/// </summary>
public class VgaDac {
    public const int VgaDacnotInitialized = 0;
    public const int VgaDacRead = 1;
    public const int VgaDacWrite = 2;
    private const int BlueIndex = 2;
    private const int GreenIndex = 1;
    private const int Redindex = 0;
    private readonly Machine _machine;

    public VgaDac(Machine machine) {
        _machine = machine;

        // Initial VGA default palette initialization
        for (int i = 0; i < Rgbs.Length; i++) {
            Rgb rgb = new() {
                R = (byte)((i >> 5 & 0x7) * 255 / 7),
                G = (byte)((i >> 2 & 0x7) * 255 / 7),
                B = (byte)((i & 0x3) * 255 / 3)
            };
            Rgbs[i] = rgb;
        }
    }

    public static byte From8bitTo6bitColor(byte color8bit) => (byte)((uint)color8bit >> 2);

    public static byte From6bitColorTo8bit(byte color6bit) => (byte)((color6bit & 0b111111) << 2);

    /// <summary>
    /// 0 = red, 1 = green, 2 = blue 
    /// </summary>
    public int Colour { get; set; }

    public int ReadIndex { get; set; }

    public Rgb[] Rgbs { get; private set; } = new Rgb[256];

    public int State { get; set; } = 1;

    public int WriteIndex { get; set; }

    public byte ReadColor() {
        Rgb rgb = Rgbs[ReadIndex];
        byte value = Colour switch {
            Redindex => rgb.R,
            GreenIndex => rgb.G,
            BlueIndex => rgb.B,
            _ => throw new InvalidColorIndexException(_machine, Colour)
        };
        Colour = (Colour + 1) % 3;
        if (Colour == 0) {
            WriteIndex++;
        }
        return value;
    }

    public override string ToString() {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }

    public void WriteColor(byte colorValue) {
        Rgb rgb = Rgbs[WriteIndex];
        switch (Colour) {
            case Redindex:
                rgb.R = colorValue;
                break;
            case GreenIndex:
                rgb.G = colorValue;
                break;
            case BlueIndex:
                rgb.B = colorValue;
                break;
            default:
                throw new InvalidColorIndexException(_machine, Colour);
        }

        Colour = (Colour + 1) % 3;
        if (Colour == 0) {
            WriteIndex++;
        }
    }
}