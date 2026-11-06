// SPDX-FileCopyrightText: 2022-2026 The DOSBox Staging Team
// SPDX-FileCopyrightText: 2002-2021 The DOSBox Team
// SPDX-License-Identifier: GPL-2.0-or-later

namespace Spice86.Core.Emulator.Devices.DirectMemoryAccess;

/// <summary>Hardware occupying ports shared with the secondary DMA controller.</summary>
public interface ISecondaryDmaPortOwner {
    /// <summary>Releases the shared ports before the secondary controller is restored.</summary>
    void ReleaseSecondaryDmaPorts();
}