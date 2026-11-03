// SPDX-FileCopyrightText: 2022-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Fixed Tandy firmware identification and video parameters from DOSBox Staging's BIOS.
/// </summary>
public sealed class TandyBiosRom : MemoryBasedDataStructure {
    /// <summary>
    /// The standard reset entry used by Tandy firmware.
    /// </summary>
    public static readonly SegmentedAddress ResetEntry = new(0xF000, 0xE05B);

    /// <summary>
    /// The basic video parameter table returned through interrupt vector 1Dh.
    /// </summary>
    public static readonly SegmentedAddress VideoParameterTable = new(0xF000, 0xF0A4);

    /// <summary>
    /// The reference BIOS's print-screen interrupt return stub.
    /// </summary>
    public static readonly SegmentedAddress PrintScreenHandler = new(0xF000, 0xFF54);

    private static ReadOnlySpan<byte> Configuration => [
        0x21, 0x42, 0x49, 0x4F, 0x53, 0x20, 0x52, 0x4F, 0x4D, 0x20, 0x76, 0x65, 0x72,
        0x73, 0x69, 0x6F, 0x6E, 0x20, 0x30, 0x32, 0x2E, 0x30, 0x30, 0x2E, 0x30, 0x30,
        0x0D, 0x0A, 0x43, 0x6F, 0x6D, 0x70, 0x61, 0x74, 0x69, 0x62, 0x69, 0x6C, 0x69,
        0x74, 0x79, 0x20, 0x53, 0x6F, 0x66, 0x74, 0x77, 0x61, 0x72, 0x65, 0x0D, 0x0A,
        0x43, 0x6F, 0x70, 0x79, 0x72, 0x69, 0x67, 0x68, 0x74, 0x20, 0x28, 0x43, 0x29,
        0x20, 0x31, 0x39, 0x38, 0x34, 0x2C, 0x31, 0x39, 0x38, 0x35, 0x2C, 0x31, 0x39,
        0x38, 0x36, 0x2C, 0x31, 0x39, 0x38, 0x37, 0x0D, 0x0A, 0x50, 0x68, 0x6F, 0x65,
        0x6E, 0x69, 0x78, 0x20, 0x53, 0x6F, 0x66, 0x74, 0x77, 0x61, 0x72, 0x65, 0x20,
        0x41, 0x73, 0x73, 0x6F, 0x63, 0x69, 0x61, 0x74, 0x65, 0x73, 0x20, 0x4C, 0x74,
        0x64, 0x2E, 0x0D, 0x0A, 0x61, 0x6E, 0x64, 0x20, 0x54, 0x61, 0x6E, 0x64, 0x79
    ];

    private static ReadOnlySpan<byte> VideoParameters => [
        0x38, 0x28, 0x2C, 0x08, 0x1F, 0x06, 0x19, 0x1C, 0x02, 0x07, 0x06, 0x07, 0, 0, 0, 0,
        0x71, 0x50, 0x58, 0x10, 0x1F, 0x06, 0x19, 0x1C, 0x02, 0x07, 0x06, 0x07, 0, 0, 0, 0,
        0x38, 0x28, 0x2C, 0x08, 0x7F, 0x06, 0x64, 0x70, 0x02, 0x01, 0x06, 0x07, 0, 0, 0, 0,
        0x71, 0x50, 0x58, 0x10, 0x3F, 0x06, 0x32, 0x38, 0x02, 0x03, 0x06, 0x07, 0, 0, 0, 0,
        0x00, 0x08, 0x00, 0x10, 0x00, 0x40, 0x00, 0x40,
        40, 40, 80, 80, 40, 40, 80, 80,
        0x2C, 0x28, 0x2D, 0x29, 0x2A, 0x2E, 0x1E, 0x29
    ];

    /// <summary>
    /// Wraps the F000h firmware segment without modifying it.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    public TandyBiosRom(IByteReaderWriter memory) : base(memory, 0xF0000) {
    }

    /// <summary>
    /// Initializes only the fixed data and reset jumps provided by the reference BIOS.
    /// </summary>
    /// <param name="resetHandler">The installed reset termination routine.</param>
    public void Initialize(SegmentedAddress resetHandler) {
        WriteFarJump(0xFFF0, ResetEntry);
        WriteFarJump(ResetEntry.Offset, resetHandler);
        WriteAscii(0xE00E, "IBM COMPATIBLE 486 BIOS COPYRIGHT The DOSBox Team.");
        WriteAscii(0xE061, "DOSBox FakeBIOS v1.0");
        WriteAscii(0xFFF5, "01/01/92");
        UInt8[0xFFFE] = 0xFF;
        UInt8[0xFFFF] = 0xFF;
        UInt8[0xFF53] = 0xCF;
        UInt8[PrintScreenHandler.Offset] = 0xCF;
        ReadOnlySpan<byte> configuration = Configuration;
        for (int index = 0; index < configuration.Length; index++) {
            UInt8[0xC000 + index] = configuration[index];
        }
        ReadOnlySpan<byte> parameters = VideoParameters;
        for (int index = 0; index < parameters.Length; index++) {
            UInt8[VideoParameterTable.Offset + index] = parameters[index];
        }
    }

    private void WriteFarJump(ushort offset, SegmentedAddress destination) {
        UInt8[offset] = 0xEA;
        UInt16[offset + 1] = destination.Offset;
        UInt16[offset + 3] = destination.Segment;
    }

    private void WriteAscii(ushort offset, string value) {
        for (int index = 0; index < value.Length; index++) {
            UInt8[offset + index] = (byte)value[index];
        }
        UInt8[offset + value.Length] = 0;
    }
}