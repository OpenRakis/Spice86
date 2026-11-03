// SPDX-FileCopyrightText: 2020-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.VGA;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Enums;
using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

/// <summary>Native BIOS mode metadata from ModeList_OTHER, including its unreachable 0Eh entry.</summary>
internal sealed record TandyVideoMode(int Id, int PixelWidth, int Columns, int Pages, int PageLength,
    int HorizontalTotal, int VerticalTotal, int HorizontalDisplay, int VerticalDisplay, int Scanlines,
    byte ModeControl, byte ExtendedControl, ushort Segment) {
    private static readonly TandyVideoMode[] Modes = [
        new(0, 320, 40, 8, 0x0800, 56, 31, 40, 25, 8, 0x2C, 0, 0xB800),
        new(1, 320, 40, 8, 0x0800, 56, 31, 40, 25, 8, 0x28, 0, 0xB800),
        new(2, 640, 80, 4, 0x1000, 113, 31, 80, 25, 8, 0x2D, 0, 0xB800),
        new(3, 640, 80, 4, 0x1000, 113, 31, 80, 25, 8, 0x29, 0, 0xB800),
        new(4, 320, 40, 1, 0x4000, 56, 127, 40, 100, 2, 0x2A, 0, 0xB800),
        new(5, 320, 40, 1, 0x4000, 56, 127, 40, 100, 2, 0x2E, 0, 0xB800),
        new(6, 640, 80, 1, 0x4000, 56, 127, 40, 100, 2, 0x1E, 0, 0xB800),
        new(8, 160, 20, 8, 0x2000, 56, 127, 40, 100, 2, 0x2A, 0x14, 0xB800),
        new(9, 320, 40, 8, 0x2000, 113, 63, 80, 50, 4, 0x2B, 0x14, 0xB800),
        new(10, 640, 80, 8, 0x2000, 113, 63, 80, 50, 4, 0x3B, 0x0C, 0xB800),
        new(14, 640, 80, 8, 0x1000, 113, 256, 80, 200, 2, 0, 0, 0xA000)
    ];

    /// <summary>Whether this is a text mode.</summary>
    public bool IsText => Id <= 3;
    /// <summary>Power-on text mode.</summary>
    public static TandyVideoMode Default => Modes[3];
    /// <summary>Native graphics pixel depth.</summary>
    public byte BitsPerPixel {
        get {
            if (IsText || Id is 8 or 9 or 14) {
                return 4;
            }
            return Id == 6 ? (byte)1 : (byte)2;
        }
    }
    /// <summary>Adapts the native metadata to existing BIOS character helpers.</summary>
    public VgaMode Mode => new(IsText ? MemoryModel.Text : MemoryModel.Cga,
        (ushort)(IsText ? Columns : PixelWidth), (ushort)(IsText ? 25 : 200),
        BitsPerPixel, 8, 8, Segment, 3.0 * PixelWidth / 800.0);

    /// <summary>Finds a mode reachable through the native BIOS entry point.</summary>
    public static TandyVideoMode? Find(int id) {
        if (id > 0x0A) {
            return null;
        }
        if (id == 7) {
            id = 0;
        }
        return Array.Find(Modes, mode => mode.Id == id);
    }
}