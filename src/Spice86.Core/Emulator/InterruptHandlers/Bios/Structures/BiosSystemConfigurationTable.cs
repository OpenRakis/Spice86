// SPDX-FileCopyrightText: 2022-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;

using Spice86.Core.Emulator.Memory.ReaderWriter;
using Spice86.Core.Emulator.ReverseEngineer.DataStructure;

/// <summary>
/// The ten-byte system configuration table returned by INT 15h/AH=C0h.
/// </summary>
public sealed class BiosSystemConfigurationTable : MemoryBasedDataStructure {
    /// <summary>
    /// Total table length including its length word.
    /// </summary>
    public const int Length = 10;

    /// <summary>
    /// The reference BIOS allocates one paragraph for this table.
    /// </summary>
    public const int AllocationSize = 16;

    /// <summary>
    /// Wraps an allocated BIOS configuration table without modifying it.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="baseAddress">The table's physical address.</param>
    public BiosSystemConfigurationTable(IByteReaderWriter memory, uint baseAddress) : base(memory, baseAddress) {
    }

    /// <summary>
    /// Writes the reference Tandy model, revision, and feature bytes.
    /// </summary>
    public void InitializeTandy() {
        UInt16[0] = Length - 2;
        UInt8[2] = 0xFF;
        UInt8[3] = 0x0A;
        UInt8[4] = 0x10;
        UInt8[5] = 0x70;
        UInt8[6] = 0x40;
        UInt8[7] = 0;
        UInt8[8] = 0;
        UInt8[9] = 0;
    }
}