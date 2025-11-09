// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

// struct _opl3_channel {
//     opl3_slot *slotz[2];/*Don't use "slots" keyword to avoid conflict with Qt applications*/
//     opl3_channel *pair;
//     opl3_chip *chip;
//     int16_t *out[4];
//
// #if OPL_ENABLE_STEREOEXT
//     int32_t leftpan;
//     int32_t rightpan;
// #endif
//
//     uint8_t chtype;
//     uint16_t f_num;
//     uint8_t block;
//     uint8_t fb;
//     uint8_t con;
//     uint8_t alg;
//     uint8_t ksv;
//     uint16_t cha, chb;
//     uint16_t chc, chd;
//     uint8_t ch_num;
// };
internal sealed class Opl3Channel {
    internal Opl3Operator[] Slotz { get; } = new Opl3Operator[2];
    internal Opl3Channel? Pair { get; set; }
    internal Opl3Chip? Chip { get; set; }
    internal ShortSignalSource[] Out { get; } = new ShortSignalSource[4];
#if OPL_ENABLE_STEREOEXT
    internal int LeftPan;
    internal int RightPan;
#endif
    internal ChannelType ChannelType;
    internal ushort FNumber;
    internal byte Block;
    internal byte Feedback;
    internal byte Connection;
    internal byte Algorithm;
    internal byte KeyScaleValue;
    internal ushort Cha;
    internal ushort Chb;
    internal ushort Chc;
    internal ushort Chd;
    internal byte ChannelNumber;
}
