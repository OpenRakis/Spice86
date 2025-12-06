// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

/// <summary>
/// Defines port numbers for OPL (FM synthesis) hardware.
/// </summary>
public interface IOplPort {
    /// <summary>
    /// Primary OPL address port number.
    /// </summary>
    const ushort PrimaryAddressPortNumber = 0x388;
    
    /// <summary>
    /// Primary OPL data port number.
    /// </summary>
    const ushort PrimaryDataPortNumber = 0x389;
    
    /// <summary>
    /// Secondary OPL address port number.
    /// </summary>
    const ushort SecondaryAddressPortNumber = 0x228;
    
    /// <summary>
    /// Secondary OPL data port number.
    /// </summary>
    const ushort SecondaryDataPortNumber = 0x229;
    
    /// <summary>
    /// AdLib Gold address port number.
    /// </summary>
    const ushort AdLibGoldAddressPortNumber = 0x38A;
    
    /// <summary>
    /// AdLib Gold data port number.
    /// </summary>
    const ushort AdLibGoldDataPortNumber = 0x38B;
}