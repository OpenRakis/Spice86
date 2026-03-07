// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

public interface IOplPort {
    const ushort PrimaryAddressPortNumber = 0x388;
    const ushort PrimaryDataPortNumber = 0x389;
    const ushort SecondaryAddressPortNumber = 0x228;
    const ushort SecondaryDataPortNumber = 0x229;
    const ushort AdLibGoldAddressPortNumber = 0x38A;
    const ushort AdLibGoldDataPortNumber = 0x38B;
}