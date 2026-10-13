// SPDX-FileCopyrightText: 2020-2025 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.Audio;

using System;

/// <summary>
/// Bit flags of the GF1 DMA control register 0x41, as written by the host.
/// </summary>
[Flags]
public enum GusDmaControl : byte {
    /// <summary>No flag set: DMA is idle.</summary>
    None = 0x00,

    /// <summary>DMA transfers are enabled.</summary>
    Enabled = 0x01,

    /// <summary>Transfer direction is GUS DRAM to host memory (recording) instead of host to DRAM.</summary>
    GusToHost = 0x02,

    /// <summary>The selected DMA channel is a 16-bit channel.</summary>
    Channel16Bit = 0x04,

    /// <summary>An IRQ is raised when the DMA channel reaches terminal count.</summary>
    RaiseIrqOnTerminalCount = 0x20,

    /// <summary>The transferred samples are 16-bit. On a register read this bit instead reports a pending terminal-count IRQ.</summary>
    Samples16Bit = 0x40,

    /// <summary>The high bit of each sample is inverted, converting between signed and unsigned PCM.</summary>
    InvertHighBit = 0x80
}
