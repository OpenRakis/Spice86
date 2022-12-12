/* Nuked OPL3
 * Copyright (C) 2013-2020 Nuke.YKT
 *
 * This file is part of Nuked OPL3.
 *
 * Nuked OPL3 is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as
 * published by the Free Software Foundation, either version 2.1
 * of the License, or (at your option) any later version.
 *
 * Nuked OPL3 is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with Nuked OPL3. If not, see <https://www.gnu.org/licenses/>.

 *  Nuked OPL3 emulator.
 *  Thanks:
 *      MAME Development Team(Jarek Burczynski, Tatsuyuki Satoh):
 *          Feedback and Rhythm part calculation information.
 *      forums.submarine.org.uk(carbon14, opl3):
 *          Tremolo and phase generator calculation information.
 *      OPLx decapsulated(Matthew Gambrell, Olli Niemitalo):
 *          OPL2 ROMs.
 *      siliconpr0n.org(John McMaster, digshadow):
 *          YMF262 and VRC VII decaps and die shots.
 *
 * version: 1.8
 */

// Enables Stereo Extensions (see Opl3Channel struct for example)
//#define OPL_ENABLE_STEREOEXT

using System;
using System.Collections.ObjectModel;

namespace Spice86.Core.Emulator.Devices.Sound;

public static class OPL3Nuked {
    public const int OPL_WRITEBUF_SIZE = 1024;
    public const int OPL_WRITEBUF_DELAY = 2;

#if OPL_ENABLE_STEREOEXT && OPL_SIN
#define _USE_MATH_DEFINES
    private static double OplSin(double x) {
        return ((int)(Math.Sin(x) * Math.Pi / 512.0)) * 65536.0;
    }
#endif
    public const int RSM_FRAC = 10;

    /// <summary>
    /// Channel types
    /// </summary>
    private enum ChType {
        ch_2op,
        ch_4op,
        ch_4op2,
        ch_drum
    }

    /// <summary>
    /// Envelope key types
    /// </summary>
    private enum EnvelopeKeyType {
        egk_norm = 0x01,
        egk_drum = 0x02
    }

    /// <summary>
    /// logsin table
    /// </summary>
    private static readonly ReadOnlyCollection<int> LogSinRom = Array.AsReadOnly(new int[] {
        0x859, 0x6c3, 0x607, 0x58b, 0x52e, 0x4e4, 0x4a6, 0x471,
        0x443, 0x41a, 0x3f5, 0x3d3, 0x3b5, 0x398, 0x37e, 0x365,
        0x34e, 0x339, 0x324, 0x311, 0x2ff, 0x2ed, 0x2dc, 0x2cd,
        0x2bd, 0x2af, 0x2a0, 0x293, 0x286, 0x279, 0x26d, 0x261,
        0x256, 0x24b, 0x240, 0x236, 0x22c, 0x222, 0x218, 0x20f,
        0x206, 0x1fd, 0x1f5, 0x1ec, 0x1e4, 0x1dc, 0x1d4, 0x1cd,
        0x1c5, 0x1be, 0x1b7, 0x1b0, 0x1a9, 0x1a2, 0x19b, 0x195,
        0x18f, 0x188, 0x182, 0x17c, 0x177, 0x171, 0x16b, 0x166,
        0x160, 0x15b, 0x155, 0x150, 0x14b, 0x146, 0x141, 0x13c,
        0x137, 0x133, 0x12e, 0x129, 0x125, 0x121, 0x11c, 0x118,
        0x114, 0x10f, 0x10b, 0x107, 0x103, 0x0ff, 0x0fb, 0x0f8,
        0x0f4, 0x0f0, 0x0ec, 0x0e9, 0x0e5, 0x0e2, 0x0de, 0x0db,
        0x0d7, 0x0d4, 0x0d1, 0x0cd, 0x0ca, 0x0c7, 0x0c4, 0x0c1,
        0x0be, 0x0bb, 0x0b8, 0x0b5, 0x0b2, 0x0af, 0x0ac, 0x0a9,
        0x0a7, 0x0a4, 0x0a1, 0x09f, 0x09c, 0x099, 0x097, 0x094,
        0x092, 0x08f, 0x08d, 0x08a, 0x088, 0x086, 0x083, 0x081,
        0x07f, 0x07d, 0x07a, 0x078, 0x076, 0x074, 0x072, 0x070,
        0x06e, 0x06c, 0x06a, 0x068, 0x066, 0x064, 0x062, 0x060,
        0x05e, 0x05c, 0x05b, 0x059, 0x057, 0x055, 0x053, 0x052,
        0x050, 0x04e, 0x04d, 0x04b, 0x04a, 0x048, 0x046, 0x045,
        0x043, 0x042, 0x040, 0x03f, 0x03e, 0x03c, 0x03b, 0x039,
        0x038, 0x037, 0x035, 0x034, 0x033, 0x031, 0x030, 0x02f,
        0x02e, 0x02d, 0x02b, 0x02a, 0x029, 0x028, 0x027, 0x026,
        0x025, 0x024, 0x023, 0x022, 0x021, 0x020, 0x01f, 0x01e,
        0x01d, 0x01c, 0x01b, 0x01a, 0x019, 0x018, 0x017, 0x017,
        0x016, 0x015, 0x014, 0x014, 0x013, 0x012, 0x011, 0x011,
        0x010, 0x00f, 0x00f, 0x00e, 0x00d, 0x00d, 0x00c, 0x00c,
        0x00b, 0x00a, 0x00a, 0x009, 0x009, 0x008, 0x008, 0x007,
        0x007, 0x007, 0x006, 0x006, 0x005, 0x005, 0x005, 0x004,
        0x004, 0x004, 0x003, 0x003, 0x003, 0x002, 0x002, 0x002,
        0x002, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001,
        0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000
    });

    /// <summary>
    /// Exp table
    /// </summary>
    private static readonly ReadOnlyCollection<int> ExpRom = Array.AsReadOnly(new int[]{
        0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4,
        0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
        0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f,
        0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
        0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e,
        0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
        0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0,
        0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
        0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695,
        0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
        0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e,
        0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
        0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609,
        0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
        0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8,
        0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
        0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589,
        0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
        0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d,
        0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
        0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514,
        0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
        0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc,
        0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
        0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8,
        0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
        0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475,
        0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
        0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445,
        0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
        0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416,
        0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400
    });

    /// <summary>
    /// freq mult table multiplied by 2
    /// 1/2, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 10, 12, 12, 15, 15
    /// </summary>
    private static readonly ReadOnlyCollection<int> mt = Array.AsReadOnly(new int[] {
        1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30
    });

    #if OPL_ENABLE_STEREOEXT
    /// <summary>
    /// stereo extension panning table
    /// </summary>
    private static int[] panpot_lut = new int[256]();
    private static byte panopt_lut_build = 0;
    #endif

    private enum EnvelopeGenNum {
        envelop_gen_num_attack,
        envelop_gen_num_decay,
        envelop_gen_num_sustain,
        envelop_gen_num_release,
    }
}

public struct Opl3Chip {
    public Opl3Chip() {
    }
    public Opl3Channel[] Channel { get; set; } = new Opl3Channel[18];
    public Opl3Slot[] Slot { get; set; } = new Opl3Slot[36];
    public ushort Timer { get; set; }
    public ulong EgTimer { get; set; }
    public byte EgTimerRem { get; set; }
    public byte EgState { get; set; }
    public byte EgAdd { get; set; }
    public byte NewM { get; set; }
    public byte Nts { get; set; }
    public byte Rhy { get; set; }
    public byte VibPos { get; set; }
    public byte VibShift { get; set; }
    public byte Tremolo { get; set; }
    public byte TremoloPos { get; set; }
    public byte TremoloShift { get; set; }
    public uint Noise { get; set; }
    public short ZeroMod { get; set; }
    public int[] MixBuff { get; set; } = new int[2];
    public byte RmHhBits2 { get; set; }
    public byte RmHhBits3 { get; set; }
    public byte RmHhBits7 { get; set; }
    public byte RmHhBits8 { get; set; }
    public byte RmTcBits3 { get; set; }
    public byte RmTcBits5 { get; set; }

#if OPL_ENABLE_STEREOEXT
    public byte StereoExt { get; set; }
#endif

    /* OPL3L */
    public int RateRatio { get; set; }
    public int SampleCnt { get; set; }
    public short[] Oldsamples { get; set; } = new short[2];

    public short[] Samples { get; set; } = new short[2];

    public ulong WritebufSampleCnt { get; set; }

    public uint WriteBufCur { get; set; }

    public uint WriteBufLast { get; set; }

    public ulong WriteBufLastTime { get; set; }

    public Opl3WriteBuf[] WriteBuf { get; set; } = new Opl3WriteBuf[OPL3Nuked.OPL_WRITEBUF_SIZE];
}

public struct Opl3Slot {
    public Opl3Channel Channel { get; set; }

    public Opl3Chip Chip { get; set; }
    public short Out { get; set; }
    public short FbMod { get; set; }
    public short Mod { get; set; }
    public short PrOut { get; set; }
    public ushort EgRout { get; set; }
    public ushort EgOut { get; set; }
    public byte EgInc { get; set; }
    public byte EgGen { get; set; }
    public byte EgRate { get; set; }
    public byte EgKsl { get; set; }
    public byte Trem { get; set; }
    public byte RegVib { get; set; }
    public byte RegType { get; set; }
    public byte RegKsr { get; set; }
    public byte RegMult { get; set; }
    public byte RegKsl { get; set; }
    public byte RegTl { get; set; }
    public byte RegAr { get; set; }
    public byte RegDr { get; set; }
    public byte RegSl { get; set; }
    public byte RegWf { get; set; }
    public byte Key { get; set; }
    public uint PgReset { get; set; }
    public uint PgPhase { get; set; }
    public ushort PgPhaseOut { get; set; }
    public byte SlotNum { get; set; }
}

public class Opl3Channel {
    public Opl3Slot[] Slots { get; set; } = new Opl3Slot[2];
    public Opl3Channel? Pair { get; set; }
    public Opl3Chip? Chip { get; set; }
    public byte ChType { get; set; }
    public ushort FNum { get; set; }
    public byte Block { get; set; }
    public byte Fb { get; set; }
    public byte Con { get; set; }
    public byte Alg { get; set; }
    public byte Ksv { get; set; }
    public ushort Cha { get; set; }
    public ushort Chb { get; set; }
    public byte ChNum { get; set; }
#if OPL_ENABLE_STEREOEXT
    public int LeftPan { get; set; }
    public int RightPan { get; set; }
#endif
}

public struct Opl3WriteBuf {
    public ulong Time { get; set; }
    public ushort Reg { get; set; }
    public byte Data { get; set; }
}
