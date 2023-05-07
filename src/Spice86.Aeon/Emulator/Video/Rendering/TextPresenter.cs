using System.Numerics;
using System.Runtime.InteropServices;

namespace Spice86.Aeon.Emulator.Video.Rendering;

using Spice86.Aeon.Emulator.Video.Modes;

/// <summary>
/// Renders text-mode graphics to a bitmap.
/// </summary>
public sealed class TextPresenter : Presenter
{
    private readonly uint consoleWidth;
    private readonly uint consoleHeight;
    private readonly uint fontHeight;
    private readonly unsafe ushort*[] pages;
    private readonly byte[] font;
    private readonly unsafe byte* videoRam;

    /// <summary>
    /// Initializes a new instance of the TextPresenter class.
    /// </summary>
    /// <param name="videoMode">VideoMode instance describing the video mode.</param>
    public TextPresenter(VideoMode videoMode) : base(videoMode)
    {
        unsafe
        {
            videoRam = (byte*)videoMode.VideoRam.ToPointer();
            byte* srcPtr = (byte*)videoMode.VideoRam.ToPointer();

            pages = new ushort*[8];
            for (int i = 0; i < pages.Length; i++)
                pages[i] = (ushort*)(srcPtr + VideoMode.DisplayPageSize * i);
        }

        consoleWidth = (uint)videoMode.Width;
        consoleHeight = (uint)videoMode.Height;
        font = videoMode.Font;
        fontHeight = (uint)videoMode.FontHeight;
    }

    /// <summary>
    /// Updates the bitmap to match the current state of the video RAM.
    /// </summary>
    protected override void DrawFrame(IntPtr destination)
    {
        unsafe
        {
            var palette = VideoMode.Palette;
            byte* internalPalette = stackalloc byte[16];
            VideoMode.InternalPalette.CopyTo(new Span<byte>(internalPalette, 16));
            uint displayPage = (uint)VideoMode.ActiveDisplayPage;

            uint* destPtr = (uint*)destination.ToPointer();

            byte* textPlane = videoRam + VideoMode.DisplayPageSize * displayPage;
            byte* attrPlane = videoRam + VideoMode.PlaneSize + VideoMode.DisplayPageSize * displayPage;

            for (uint y = 0; y < consoleHeight; y++)
            {
                for (uint x = 0; x < consoleWidth; x++)
                {
                    uint srcOffset = y * consoleWidth + x;

                    uint* dest = destPtr + y * consoleWidth * 8 * fontHeight + x * 8;
                    DrawCharacter(dest, textPlane[srcOffset], palette[internalPalette[attrPlane[srcOffset] & 0x0F]], palette[internalPalette[attrPlane[srcOffset] >> 4]]);
                }
            }
        }
    }

    /// <summary>
    /// Draws a single character to the bitmap.
    /// </summary>
    /// <param name="dest">Pointer in bitmap to top-left corner of the character.</param>
    /// <param name="index">Index of the character.</param>
    /// <param name="foregroundColor">Foreground color of the character.</param>
    /// <param name="backgroundColor">Background color of the character.</param>
    private unsafe void DrawCharacter(uint* dest, byte index, uint foregroundColor, uint backgroundColor)
    {
        if (Vector.IsHardwareAccelerated)
        {
            ReadOnlySpan<uint> indexes = stackalloc uint[] { 1 << 7, 1 << 6, 1 << 5, 1 << 4, 1 << 3, 1 << 2, 1 << 1, 1 << 0 };
            var indexVector = MemoryMarshal.Cast<uint, Vector<uint>>(indexes);
            var foregroundVector = new Vector<uint>(foregroundColor);
            var backgroundVector = new Vector<uint>(backgroundColor);

            for (int y = 0; y < fontHeight; y++)
            {
                byte current = font[(index * fontHeight) + y];
                var currentVector = new Vector<uint>(current);

                int x = 0;

                for (int i = 0; i < indexVector.Length; i++)
                {
                    var maskResult = Vector.BitwiseAnd(currentVector, indexVector[i]);
                    var equalsResult = Vector.Equals(maskResult, indexVector[i]);
                    var result = Vector.ConditionalSelect(equalsResult, foregroundVector, backgroundVector);
                    for (int j = 0; j < Vector<uint>.Count; j++)
                        dest[x + j] = result[j];

                    x += Vector<uint>.Count;
                }

                dest += consoleWidth * 8;
            }
        }
        else
        {
            for (int y = 0; y < fontHeight; y++)
            {
                byte current = font[(index * fontHeight) + y];

                for (int x = 0; x < 8; x++)
                    dest[x] = (current & (1 << (7 - x))) != 0 ? foregroundColor : backgroundColor;

                dest += consoleWidth * 8;
            }
        }
    }
}