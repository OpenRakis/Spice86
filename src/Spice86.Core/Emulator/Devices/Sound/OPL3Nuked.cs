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

/* Quirk: Some FM channels are output one sample later on the left side than the right. */
#define OPL_QUIRK_CHANNELSAMPLEDELAY
// Enables Stereo Extensions (see Opl3Channel struct for example)
//#undef OPL_QUIRK_CHANNELSAMPLEDELAY
//#define OPL_ENABLE_STEREOEXT

using System.Collections.ObjectModel;
namespace Spice86.Core.Emulator.Devices.Sound;

using Spice86.Shared.Emulator.Errors;

public static class Opl3Nuked {
    public const int OplWriteBufSize = 1024;
    public const int OplWriteBufDelay = 2;

#if OPL_ENABLE_STEREOEXT && OPL_SIN
#define _USE_MATH_DEFINES
    private static double OplSin(double x) {
        return ((int)(Math.Sin(x) * Math.Pi / 512.0)) * 65536.0;
    }
#endif
    public const int RsmFrac = 10;

    /// <summary>
    /// Channel types
    /// </summary>
    private enum ChType {
        Ch2Op,
        Ch4Op,
        Ch4Op2,
        ChDrum
    }

    /// <summary>
    /// Envelope key types
    /// </summary>
    private enum EnvelopeKeyType {
        EgkNorm = 0x01,
        EgkDrum = 0x02
    }

    /// <summary>
    /// logsin table
    /// </summary>
    private static readonly ReadOnlyCollection<ushort> LogSinRom = Array.AsReadOnly(new ushort[] {
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
    private static readonly ReadOnlyCollection<uint> ExpRom = Array.AsReadOnly(new uint[]{
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
    private static readonly ReadOnlyCollection<int> Mt = Array.AsReadOnly(new[] {
        1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30
    });

    /// <summary>
    /// KSL Table
    /// </summary>
    private static readonly ReadOnlyCollection<byte> KslRom = Array.AsReadOnly(new byte[] {
        0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64
    });

    private static readonly ReadOnlyCollection<byte> KslShift = Array.AsReadOnly(new byte[] {
        8, 1, 2, 0
    });

    /// <summary>
    /// envelope generator constants
    /// </summary>
    private static readonly ReadOnlyCollection<byte[]> EgIncStep = Array.AsReadOnly(new[] {
        new byte[]{ 0, 0, 0, 0 },
        new byte[]{ 1, 0, 0, 0 },
        new byte[]{ 1, 0, 1, 0 },
        new byte[]{ 1, 1, 1, 0 }
    });

    /// <summary>
    /// address decoding
    /// </summary>
    private static readonly ReadOnlyCollection<sbyte> AdSlot = Array.AsReadOnly(new sbyte[] {
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1,
        12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1
    });

    private static readonly ReadOnlyCollection<byte> ChSlot = Array.AsReadOnly(new byte[] {
        0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32
    });

#if OPL_ENABLE_STEREOEXT
    /// <summary>
    /// stereo extension panning table
    /// </summary>
    private static int[] PanpotLut = new int[256]();
    private const byte PanpotLutBuild = 0;
#endif

    /*
    * Envelope generator
    */
    private static short Opl3EnvelopeCalcExp(int level) {
        if (level > 0x1fff) {
            level = 0x1fff;
        }
        return (short)((ExpRom[level & 0xff] << 1) >> (level >> 8));
    }

    private static short Opl3EnvelopeCalcSin0(ushort phase, ushort envelope) {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) >= 1) {
            neg = 0xffff;
        }
        ushort output;
        if ((phase & 0x100) >= 1) {
            output = LogSinRom[(phase & 0xff) ^ 0xff];
        } else {
            output = LogSinRom[phase & 0xff];
        }
        return (short)(Opl3EnvelopeCalcExp(output + (envelope << 3)) ^ neg);
    }

    private static short Opl3EnvelopeCalcSin1(ushort phase, ushort envelope) {
        phase &= 0x3f;
        ushort output;
        if ((phase & 0x200) >= 1) {
            output = 0x1000;
        } else if ((phase & 0x100) >= 1) {
            output = LogSinRom[(phase & 0xff) ^ 0xff];
        } else {
            output = LogSinRom[phase & 0xff];
        }
        return Opl3EnvelopeCalcExp(output + (envelope << 3));
    }

    private static short Opl3EnvelopeCalcSin2(ushort phase, ushort envelope) {
        phase &= 0x3ff;
        ushort output;
        if ((phase & 0x100) >= 1) {
            output = LogSinRom[(phase & 0xff) ^ 0xff];
        } else {
            output = LogSinRom[phase & 0xff];
        }
        return Opl3EnvelopeCalcExp(output + (envelope << 3));
    }

    private static short Opl3EnvelopeCalcSin3(ushort phase, ushort envelope) {
        phase &= 0x3ff;
        ushort output;
        if ((phase & 0x100) >= 1) {
            output = 0x1000;
        } else {
            output = LogSinRom[phase & 0xff];
        }
        return Opl3EnvelopeCalcExp(output + (envelope << 3));
    }

    private static short Opl3EnvelopeCalcSin4(ushort phase, ushort envelope) {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x300) == 0x100) {
            neg = 0xffff;
        }
        ushort output;
        if ((phase & 0x200) >= 1) {
            output = 0x1000;
        } else if ((phase & 0x80) >= 1) {
            output = LogSinRom[((phase ^ 0xff) << 1) & 0xff];
        } else {
            output = LogSinRom[(phase << 1) & 0xff];
        }
        return (short)(Opl3EnvelopeCalcExp(output + (envelope << 3)) ^ neg);
    }

    private static short Opl3EnvelopeCalcSin5(ushort phase, ushort envelope) {
        phase &= 0x3ff;
        ushort output;
        if ((phase & 0x200) >= 1) {
            output = 0x1000;
        } else if ((phase & 0x80) >= 1) {
            output = LogSinRom[((phase ^ 0xff) << 1) & 0xff];
        } else {
            output = LogSinRom[(phase << 1) & 0xff];
        }
        return Opl3EnvelopeCalcExp(output + (envelope << 3));
    }

    private static short Opl3EnvelopeCalcSin6(ushort phase, ushort envelope) {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) >= 1) {
            neg = 0xffff;
        }
        return (short)(Opl3EnvelopeCalcExp(envelope << 3) ^ neg);
    }

    private static short Opl3EnvelopeCalcSin7(ushort phase, ushort envelope) {
        ushort neg = 0;
        phase &= 0x3ff;
        if ((phase & 0x200) >= 1) {
            neg = 0xffff;
            phase = (ushort)((phase & 0x1ff) ^ 0x1ff);
        }
        ushort output = (ushort)(phase << 3);
        return (short)(Opl3EnvelopeCalcExp(output + (envelope << 3)) ^ neg);
    }

    private static readonly ReadOnlyCollection<Func<ushort, ushort, short>> EnvelopeSin = Array.AsReadOnly(new Func<ushort, ushort, short>[]{
        (x, y) => Opl3EnvelopeCalcSin0(x, y),
        (x, y) => Opl3EnvelopeCalcSin1(x, y),
        (x, y) => Opl3EnvelopeCalcSin2(x, y),
        (x, y) => Opl3EnvelopeCalcSin3(x, y),
        (x, y) => Opl3EnvelopeCalcSin4(x, y),
        (x, y) => Opl3EnvelopeCalcSin5(x, y),
        (x, y) => Opl3EnvelopeCalcSin6(x, y),
        (x, y) => Opl3EnvelopeCalcSin7(x, y),
    });

    private enum EnvelopeGenNum {
        Attack,
        Decay,
        Sustain,
        Release,
    }

    private static void Opl3EnvelopeUpdateKsl(ref Opl3Slot slot) {
        short ksl = (short)((KslRom[slot.Channel.FNum >> 6] << 2)
            - ((0x08 - slot.Channel.Block) << 5));
        if (ksl < 0) {
            ksl = 0;
        }
        slot.EgKsl = (byte)ksl;
    }

    private static void Opl3EnvelopeCalc(ref Opl3Slot slot) {
        byte nonzero;
        byte rate;
        byte rateHi;
        byte rateLo;
        byte regRate = 0;
        byte ks;
        byte egShift, shift;
        ushort egRout;
        short egInc;
        byte egOff;
        byte reset = 0;
        slot.EgOut = (ushort)(slot.EgRout + (slot.RegTl << 2)
            + (slot.EgKsl >> KslShift[slot.RegKsl]) + slot.Trem);
        if (slot.Key > 0 && slot.EgGen == (int)EnvelopeGenNum.Release) {
            reset = 1;
            regRate = slot.RegAr;
        } else {
            switch (slot.EgGen) {
                case (byte)EnvelopeGenNum.Attack:
                    regRate = slot.RegAr;
                    break;
                case (byte)EnvelopeGenNum.Decay:
                    regRate = slot.RegDr;
                    break;
                case (byte)EnvelopeGenNum.Sustain:
                    if (slot.RegType <= 0) {
                        regRate = slot.RegRr;
                    }
                    break;
                case (byte)EnvelopeGenNum.Release:
                    regRate = slot.RegRr;
                    break;
            }
        }
        slot.PgReset = reset;
        ks = (byte)(slot.Channel.Ksv >> ((slot.RegKsr ^ 1) << 1));
        nonzero = (byte)(regRate != 0 ? 1 : 0);
        rate = (byte)(ks + (regRate << 2));
        rateHi = (byte)(rate >> 2);
        rateLo = (byte)(rate & 0x03);
        if ((rateHi & 0x10) > 0) {
            rateHi = 0x0f;
        }
        egShift = (byte)(rateHi + slot.Chip.EgAdd);
        shift = 0;
        if (nonzero > 0) {
            if (rateHi < 12) {
                if (slot.Chip.EgState > 0) {
                    switch (egShift) {
                        case 12:
                            shift = 1;
                            break;
                        case 13:
                            shift = (byte)((rateLo >> 1) & 0x01);
                            break;
                        case 14:
                            shift = (byte)(rateLo & 0x01);
                            break;
                    }
                }
            } else {
                shift = (byte)((rateHi & 0x03) + EgIncStep[rateLo][slot.Chip.Timer & 0x03]);
                if ((shift & 0x04) > 0) {
                    shift = 0x03;
                }
                if (shift <= 0) {
                    shift = slot.Chip.EgState;
                }
            }
        }
        egRout = slot.EgRout;
        egInc = 0;
        egOff = 0;
        /* Instant attack */
        if (reset > 0 && rateHi == 0x0f) {
            egRout = 0x00;
        }
        /* Envelope off */
        if ((slot.EgRout & 0x1f8) == 0x1f8) {
            egOff = 1;
        }
        if (slot.EgGen != (byte)EnvelopeGenNum.Attack && reset <= 0 && egOff > 0) {
            egRout = 0x1ff;
        }
        switch (slot.EgGen) {
            case (byte)EnvelopeGenNum.Attack:
                if (slot.EgRout <= 0) {
                    slot.EgGen = (byte)EnvelopeGenNum.Decay;
                } else if (slot.Key > 0 && shift > 0 && rateHi != 0x0f) {
                    egInc = (short)(~slot.EgRout >> (4 - shift));
                }
                break;
            case (byte)EnvelopeGenNum.Decay:
                if ((slot.EgRout >> 4) == slot.RegSl) {
                    slot.EgGen = (byte)EnvelopeGenNum.Sustain;
                } else if (egOff <= 0 && reset <= 0 && shift > 0) {
                    egInc = (short)(1 << (shift - 1));
                }
                break;
            case (byte)EnvelopeGenNum.Sustain:
            case (byte)EnvelopeGenNum.Release:
                if (egOff <= 0 && reset <= 0 && shift > 0) {
                    egInc = (short)(1 << (shift - 1));
                }
                break;
        }
        slot.EgRout = (ushort)((egRout + egInc) & 0x1ff);
        /* Key off */
        if (reset > 0) {
            slot.EgGen = (byte)EnvelopeGenNum.Attack;
        }
        if (slot.Key <= 0) {
            slot.EgGen = (byte)EnvelopeGenNum.Release;
        }
    }

    private static void OPL3EnvelopeKeyOn(ref Opl3Slot slot, byte type) {
        slot.Key |= type;
    }

    private static void Opl3EnvelopeKeyOff(ref Opl3Slot slot, byte type) {
        slot.Key &= (byte)~type;
    }

    /*
        Phase Generator
    */

    private static void Opl3PhaseGenerate(ref Opl3Slot slot) {
        Opl3Chip chip;
        ushort fNum;
        uint basefreq;
        byte rmXor, nBit;
        uint noise;
        ushort phase;

        chip = slot.Chip;
        fNum = slot.Channel.FNum;
        if (slot.RegVib > 0) {
            sbyte range;
            byte vibpos;

            range = (sbyte)((fNum >> 7) & 7);
            vibpos = slot.Chip.VibPos;

            if ((vibpos & 3) <= 0) {
                range = 0;
            } else if ((vibpos & 1) > 0) {
                range >>= 1;
            }
            range >>= slot.Chip.VibShift;

            if ((vibpos & 4) > 0) {
                range = (sbyte)-range;
            }
            fNum = (ushort)(fNum + range);
        }
        basefreq = (uint)((fNum << slot.Channel.Block) >> 1);
        phase = (ushort)(slot.PgPhase >> 9);
        if (slot.PgReset > 0) {
            slot.PgPhase = 0;
        }
        slot.PgPhase = (uint)(slot.PgPhase + (basefreq * Mt[slot.RegMult]) >> 1);
        /* Rhythm mode */
        noise = chip.Noise;
        slot.PgPhaseOut = phase;
        if (slot.SlotNum == 13) /* hh */
        {
            chip.RmHhBits2 = (byte)((phase >> 2) & 1);
            chip.RmHhBits3 = (byte)((phase >> 3) & 1);
            chip.RmHhBits7 = (byte)((phase >> 7) & 1);
            chip.RmHhBits8 = (byte)((phase >> 8) & 1);
        }
        if (slot.SlotNum == 17 && (chip.Rhy & 0x20) > 0) /* tc */
        {
            chip.RmTcBits3 = (byte)((phase >> 3) & 1);
            chip.RmTcBits5 = (byte)((phase >> 5) & 1);
        }
        if ((chip.Rhy & 0x20) > 0) {
            rmXor = (byte)((chip.RmHhBits2 ^ chip.RmHhBits7)
                | (chip.RmHhBits3 ^ chip.RmTcBits5)
                | (chip.RmTcBits3 ^ chip.RmTcBits5));
            switch (slot.SlotNum) {
                case 13: /* hh */
                    slot.PgPhaseOut = (ushort)(rmXor << 9);
                    if ((rmXor ^ (noise & 1)) > 0) {
                        slot.PgPhaseOut |= 0xd0;
                    } else {
                        slot.PgPhaseOut |= 0x34;
                    }
                    break;
                case 16: /* sd */
                    slot.PgPhaseOut = (ushort)((chip.RmHhBits8 << 9)
                        | ((chip.RmHhBits8 ^ (ushort)(noise & 1)) << 8));
                    break;
                case 17: /* tc */
                    slot.PgPhaseOut = (ushort)((rmXor << 9) | 0x80);
                    break;
            }
        }
        nBit = (byte)(((noise >> 14) ^ noise) & 0x01);
        chip.Noise = (uint)(((ushort)(noise >> 1)) | ((ushort)(nBit << 22)));
    }

    /*
        Slot
    */

    private static void Opl3SlotWrite20(ref Opl3Slot slot, byte data) {
        if (((data >> 7) & 0x01) > 0) {
            slot.Trem = slot.Chip.Tremolo;
        } else {
            slot.Trem = (byte)slot.Chip.ZeroMod;
        }
        slot.RegVib = (byte)((data >> 6) & 0x01);
        slot.RegType = (byte)((data >> 5) & 0x01);
        slot.RegKsr = (byte)((data >> 4) & 0x01);
        slot.RegMult = (byte)(data & 0x0f);
    }

    private static void Opl3SlotWrite40(ref Opl3Slot slot, byte data) {
        slot.RegKsl = (byte)((data >> 6) & 0x03);
        slot.RegTl = (byte)(data & 0x3f);
        Opl3EnvelopeUpdateKsl(ref slot);
    }

    private static void Opl3SlotWrite60(ref Opl3Slot slot, byte data) {
        slot.RegSl = (byte)((data >> 4) & 0x0f);
        if (slot.RegSl == 0x0f) {
            slot.RegSl = 0x1f;
        }
        slot.RegRr = (byte)(data & 0x0f);
    }

    private static void Opl3SlotWrite80(ref Opl3Slot slot, byte data) {
        slot.RegSl = (byte)((data >> 4) & 0x0f);
        if (slot.RegSl == 0x0f) {
            slot.RegSl = 0x1f;
        }
        slot.RegRr = (byte)(data & 0x0f);
    }

    private static void Opl3SlotWriteE0(ref Opl3Slot slot, byte data) {
        slot.RegWf = (byte)(data & 0x07);
        if (slot.Chip.NewM == 0x00) {
            slot.RegWf &= 0x03;
        }
    }

    private static void Opl3SlotGenerate(ref Opl3Slot slot) {
        slot.Out = EnvelopeSin[slot.RegWf]((ushort)(slot.PgPhaseOut + slot.Mod), slot.EgOut);
    }

    private static void Opl3SlotCalcFb(ref Opl3Slot slot) {
        if (slot.Channel.Fb != 0x00) {
            slot.FbMod = (short)((slot.PrOut + slot.Out) >> (0x09 - slot.Channel.Fb));
        } else {
            slot.FbMod = 0;
        }
        slot.PrOut = slot.Out;
    }

    /*
        Channel
    */

    private static void Opl3ChannelSetupAlg(Opl3Channel channel) {
        ArgumentNullException.ThrowIfNull(channel);
        channel.Pair ??= new();
        ArgumentNullException.ThrowIfNull(channel.Pair);
        if (channel.ChType == (byte)ChType.ChDrum) {
            if (channel.ChNum is 7 or 8) {
                channel.Slots[0].Mod = channel.Chip.ZeroMod;
                channel.Slots[1].Mod = channel.Chip.ZeroMod;
                return;
            }
            switch (channel.Alg & 0x01) {
                case 0x00:
                    channel.Slots[0].Mod = channel.Slots[0].FbMod;
                    channel.Slots[1].Mod = channel.Slots[0].Out;
                    break;
                case 0x01:
                    channel.Slots[0].Mod = channel.Slots[0].FbMod;
                    channel.Slots[1].Mod = channel.Chip.ZeroMod;
                    break;
            }
            return;
        }
        if ((channel.Alg & 0x08) > 0) {
            return;
        }
        if ((channel.Alg & 0x04) > 0) {
            channel.Pair.Out[0] = channel.Chip.ZeroMod;
            channel.Pair.Out[1] = channel.Chip.ZeroMod;
            channel.Pair.Out[2] = channel.Chip.ZeroMod;
            channel.Pair.Out[3] = channel.Chip.ZeroMod;
            switch (channel.Alg & 0x03) {
                case 0x00:
                    channel.Pair.Slots[0].Mod = channel.Pair.Slots[0].FbMod;
                    channel.Pair.Slots[1].Mod = channel.Pair.Slots[0].Out;
                    channel.Slots[0].Mod = channel.Pair.Slots[1].Out;
                    channel.Slots[1].Mod = channel.Slots[0].Out;
                    channel.Out[0] = channel.Slots[1].Out;
                    channel.Out[1] = channel.Chip.ZeroMod;
                    channel.Out[2] = channel.Chip.ZeroMod;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
                case 0x01:
                    channel.Pair.Slots[0].Mod = channel.Pair.Slots[0].FbMod;
                    channel.Pair.Slots[1].Mod = channel.Pair.Slots[0].Out;
                    channel.Slots[0].Mod = channel.Chip.ZeroMod;
                    channel.Slots[1].Mod = channel.Slots[0].Out;
                    channel.Out[0] = channel.Pair.Slots[1].Out;
                    channel.Out[1] = channel.Slots[1].Out;
                    channel.Out[2] = channel.Chip.ZeroMod;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
                case 0x02:
                    channel.Pair.Slots[0].Mod = channel.Pair.Slots[0].FbMod;
                    channel.Pair.Slots[1].Mod = channel.Chip.ZeroMod;
                    channel.Slots[0].Mod = channel.Pair.Slots[1].Out;
                    channel.Slots[1].Mod = channel.Slots[0].Out;
                    channel.Out[0] = channel.Pair.Slots[0].Out;
                    channel.Out[1] = channel.Slots[1].Out;
                    channel.Out[2] = channel.Chip.ZeroMod;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
                case 0x03:
                    channel.Pair.Slots[0].Mod = channel.Pair.Slots[0].FbMod;
                    channel.Pair.Slots[1].Mod = channel.Chip.ZeroMod;
                    channel.Slots[0].Mod = channel.Pair.Slots[1].Out;
                    channel.Slots[1].Mod = channel.Chip.ZeroMod;
                    channel.Out[0] = channel.Pair.Slots[0].Out;
                    channel.Out[1] = channel.Slots[0].Out;
                    channel.Out[2] = channel.Slots[1].Out;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
            }
        } else {
            switch (channel.Alg & 0x01) {
                case 0x00:
                    channel.Slots[0].Mod = channel.Slots[0].FbMod;
                    channel.Slots[1].Mod = channel.Slots[0].Out;
                    channel.Out[0] = channel.Slots[1].Out;
                    channel.Out[1] = channel.Chip.ZeroMod;
                    channel.Out[2] = channel.Chip.ZeroMod;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
                case 0x01:
                    channel.Slots[0].Mod = channel.Slots[0].FbMod;
                    channel.Slots[1].Mod = channel.Chip.ZeroMod;
                    channel.Out[0] = channel.Slots[0].Out;
                    channel.Out[1] = channel.Slots[1].Out;
                    channel.Out[2] = channel.Chip.ZeroMod;
                    channel.Out[3] = channel.Chip.ZeroMod;
                    break;
            }
        }
    }

    private static void Opl3ChannelUpdateRhytm(ref Opl3Chip chip, byte data) {
        Opl3Channel channel6;
        Opl3Channel channel7;
        Opl3Channel channel8;
        byte chNum;

        chip.Rhy = (byte)(data & 0x3f);
        if ((chip.Rhy & 0x20) > 0) {
            channel6 = chip.Channel[6];
            channel7 = chip.Channel[7];
            channel8 = chip.Channel[8];
            channel6.Out[0] = channel6.Slots[1].Out;
            channel6.Out[1] = channel6.Slots[1].Out;
            channel6.Out[2] = chip.ZeroMod;
            channel6.Out[3] = chip.ZeroMod;
            channel7.Out[0] = channel7.Slots[0].Out;
            channel7.Out[1] = channel7.Slots[0].Out;
            channel7.Out[2] = channel7.Slots[1].Out;
            channel7.Out[3] = channel7.Slots[1].Out;
            channel8.Out[0] = channel8.Slots[0].Out;
            channel8.Out[1] = channel8.Slots[0].Out;
            channel8.Out[2] = channel8.Slots[1].Out;
            channel8.Out[3] = channel8.Slots[1].Out;
            for (chNum = 6; chNum < 9; chNum++) {
                chip.Channel[chNum].ChType = (byte)ChType.ChDrum;
            }
            Opl3ChannelSetupAlg(channel6);
            Opl3ChannelSetupAlg(channel7);
            Opl3ChannelSetupAlg(channel8);
            /* hh */
            if ((chip.Rhy & 0x01) > 0) {
                OPL3EnvelopeKeyOn(ref channel7.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
            } else {
                Opl3EnvelopeKeyOff(ref channel7.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
            }
            /* tc */
            if ((chip.Rhy & 0x02) > 0) {
                OPL3EnvelopeKeyOn(ref channel8.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            } else {
                Opl3EnvelopeKeyOff(ref channel8.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            }
            /* tom */
            if ((chip.Rhy & 0x04) > 0) {
                OPL3EnvelopeKeyOn(ref channel8.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
            } else {
                Opl3EnvelopeKeyOff(ref channel8.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
            }
            /* sd */
            if ((chip.Rhy & 0x08) > 0) {
                OPL3EnvelopeKeyOn(ref channel7.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            } else {
                Opl3EnvelopeKeyOff(ref channel7.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            }
            /* bd */
            if ((chip.Rhy & 0x10) > 0) {
                OPL3EnvelopeKeyOn(ref channel6.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
                OPL3EnvelopeKeyOn(ref channel6.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            } else {
                Opl3EnvelopeKeyOff(ref channel6.Slots[0], (byte)EnvelopeKeyType.EgkDrum);
                Opl3EnvelopeKeyOff(ref channel6.Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            }
        } else {
            for (chNum = 6; chNum < 9; chNum++) {
                chip.Channel[chNum].ChType = (byte)ChType.Ch2Op;
                Opl3ChannelSetupAlg(chip.Channel[chNum]);
                Opl3EnvelopeKeyOff(ref chip.Channel[chNum].Slots[0], (byte)EnvelopeKeyType.EgkDrum);
                Opl3EnvelopeKeyOff(ref chip.Channel[chNum].Slots[1], (byte)EnvelopeKeyType.EgkDrum);
            }
        }
    }

    private static void Opl3ChannelWriteA0(Opl3Channel channel, byte data) {
        if (channel.Chip.NewM > 0 && channel.ChType == (byte)ChType.Ch4Op2) {
            return;
        }
        channel.FNum = (ushort)((channel.FNum & 0x300) | data);
        channel.Ksv = (byte)((channel.Block << 1)
                    | ((channel.FNum >> (0x09 - channel.Chip.Nts)) & 0x01));
        Opl3EnvelopeUpdateKsl(ref channel.Slots[0]);
        Opl3EnvelopeUpdateKsl(ref channel.Slots[1]);
        if (channel.Pair is not null &&
            channel.Chip.NewM > 0 && channel.ChType == (byte)ChType.Ch4Op) {
            channel.Pair.FNum = channel.FNum;
            channel.Pair.Ksv = channel.Ksv;
            Opl3EnvelopeUpdateKsl(ref channel.Pair.Slots[0]);
            Opl3EnvelopeUpdateKsl(ref channel.Pair.Slots[1]);
        }
    }

    private static void Opl3ChannelWriteB0(Opl3Channel channel, byte data) {
        if (channel.Chip.NewM > 0 && channel.ChType == (byte)ChType.Ch4Op2) {
            return;
        }
        channel.FNum = (ushort)((channel.FNum & 0xff) | ((data & 0x03) << 8));
        channel.Block = (byte)((data >> 2) & 0x07);
        channel.Ksv = (byte)((channel.Block << 1)
                    | ((channel.FNum >> (0x09 - channel.Chip.Nts)) & 0x01));
        Opl3EnvelopeUpdateKsl(ref channel.Slots[0]);
        Opl3EnvelopeUpdateKsl(ref channel.Slots[1]);
        if (channel.Pair is not null &&
            channel.Chip.NewM > 0 && channel.ChType == (byte)ChType.Ch4Op) {
            channel.Pair.FNum = channel.FNum;
            channel.Pair.Block = channel.Block;
            channel.Pair.Ksv = channel.Ksv;
            Opl3EnvelopeUpdateKsl(ref channel.Pair.Slots[0]);
            Opl3EnvelopeUpdateKsl(ref channel.Pair.Slots[1]);
        }
    }

    private static void Opl3ChannelWriteC0(Opl3Channel channel, byte data) {
        channel.Fb = (byte)((data & 0x0e) >> 1);
        channel.Con = (byte)(data & 0x01);
        channel.Alg = channel.Con;
        if (channel.Pair is not null && channel.Chip.NewM > 0) {
            if (channel.ChType == (byte)ChType.Ch4Op) {
                channel.Pair.Alg = (byte)(0x04 | (channel.Con << 1) | (channel.Pair.Con));
                channel.Alg = 0x08;
                Opl3ChannelSetupAlg(channel.Pair);
            } else if (channel.ChType == (byte)ChType.Ch4Op2) {
                channel.Alg = (byte)(0x04 | (channel.Pair.Con << 1) | (channel.Con));
                channel.Pair.Alg = 0x08;
                Opl3ChannelSetupAlg(channel);
            } else {
                Opl3ChannelSetupAlg(channel);
            }
        } else {
            Opl3ChannelSetupAlg(channel);
        }
        if (channel.Chip.NewM > 0) {
            channel.Cha = (ushort)(((data >> 4) & 0x01) > 0 ? ~0 : 0);
            channel.Chb = (ushort)(((data >> 5) & 0x01) > 0 ? ~0 : 0);
        } else {
            channel.Cha = channel.Chb = unchecked((ushort)~0);
        }
#if OPL_ENABLE_STEREOEXT
        if (!channel.Chip.StereoExt > 0)
        {
            channel.LeftPan = channel.Cha << 16;
            channel.RightPan = channel.Chb << 16;
        }
#endif
    }

#if OPL_ENABLE_STEREOEXT
    private static void OPL3ChannelWriteD0(Opl3Channel channel, byte data)
    {
        if (channel.Chip.StereoExt > 0)
        {
            channel.LeftPan = PanpotLut[data ^ 0xff];
            channel.RightPan = PanpotLut[data];
        }
    }
#endif

    private static void OPL3ChannelKeyOn(Opl3Channel channel) {
        if (channel.Chip.NewM > 0 && channel.Pair is not null) {
            if (channel.ChType == (byte)ChType.Ch4Op) {
                OPL3EnvelopeKeyOn(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                OPL3EnvelopeKeyOn(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
                OPL3EnvelopeKeyOn(ref channel.Pair.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                OPL3EnvelopeKeyOn(ref channel.Pair.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
            } else if (channel.ChType is ((byte)ChType.Ch2Op) or ((byte)ChType.ChDrum)) {
                OPL3EnvelopeKeyOn(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                OPL3EnvelopeKeyOn(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
            }
        } else {
            OPL3EnvelopeKeyOn(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
            OPL3EnvelopeKeyOn(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
        }
    }

    private static void Opl3ChannelKeyOff(Opl3Channel channel) {
        if (channel.Chip.NewM > 0 && channel.Pair is not null) {
            if (channel.ChType == (byte)ChType.Ch4Op) {
                Opl3EnvelopeKeyOff(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                Opl3EnvelopeKeyOff(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
                Opl3EnvelopeKeyOff(ref channel.Pair.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                Opl3EnvelopeKeyOff(ref channel.Pair.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
            } else if (channel.ChType is ((byte)ChType.Ch2Op) or ((byte)ChType.ChDrum)) {
                Opl3EnvelopeKeyOff(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
                Opl3EnvelopeKeyOff(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
            }
        } else {
            Opl3EnvelopeKeyOff(ref channel.Slots[0], (byte)EnvelopeKeyType.EgkNorm);
            Opl3EnvelopeKeyOff(ref channel.Slots[1], (byte)EnvelopeKeyType.EgkNorm);
        }
    }

    private static void Opl3ChannelSet4Op(ref Opl3Chip chip, byte data) {
        byte bit;
        byte chnum;
        for (bit = 0; bit < 6; bit++) {
            chnum = bit;
            if (bit >= 3) {
                chnum += 9 - 3;
            }
            if (((data >> bit) & 0x01) > 0) {
                chip.Channel[chnum].ChType = (byte)ChType.Ch4Op;
                chip.Channel[chnum + 3].ChType = (byte)ChType.Ch4Op2;
            } else {
                chip.Channel[chnum].ChType = (byte)ChType.Ch2Op;
                chip.Channel[chnum + 3].ChType = (byte)ChType.Ch2Op;
            }
        }
    }

    private static short Opl3ClipSample(int sample) {
        if (sample > 32767) {
            sample = 32767;
        } else if (sample < -32768) {
            sample = -32768;
        }
        return (short)sample;
    }

    private static void Opl3ProcessSlot(ref Opl3Slot slot) {
        Opl3SlotCalcFb(ref slot);
        Opl3EnvelopeCalc(ref slot);
        Opl3PhaseGenerate(ref slot);
        Opl3SlotGenerate(ref slot);
    }

    public static void Opl3Generate(ref Opl3Chip chip, short[] buf) {
        Opl3Channel channel;
        Opl3WriteBuf writebuf;
        short[] output;
        int mix;
        byte ii;
        short accm;
        byte shift = 0;

        buf[1] = Opl3ClipSample(chip.MixBuff[1]);

#if OPL_QUIRK_CHANNELSAMPLEDELAY
        for (ii = 0; ii < 15; ii++)
#else
        for (ii = 0; ii < 36; ii++)
#endif
        {
            Opl3ProcessSlot(ref chip.Slot[ii]);
        }

        mix = 0;
        for (ii = 0; ii < 18; ii++) {
            channel = chip.Channel[ii];
            output = channel.Out;
            accm = (short)(output[0] + output[1] + output[2] + output[3]);
#if OPL_ENABLE_STEREOEXT
            mix += (short)((accm * channel.LeftPan) >> 16);
#else
            mix += (short)(accm & channel.Cha);
#endif
        }
        chip.MixBuff[0] = mix;

#if OPL_QUIRK_CHANNELSAMPLEDELAY
        for (ii = 15; ii < 18; ii++)
        {
            Opl3ProcessSlot(ref chip.Slot[ii]);
        }
#endif

        buf[0] = Opl3ClipSample(chip.MixBuff[0]);

#if OPL_QUIRK_CHANNELSAMPLEDELAY
        for (ii = 18; ii < 33; ii++)
        {
            Opl3ProcessSlot(ref chip.Slot[ii]);
        }
#endif

        mix = 0;
        for (ii = 0; ii < 18; ii++) {
            channel = chip.Channel[ii];
            output = channel.Out;
            accm = (short)(output[0] + output[1] + output[2] + output[3]);
#if OPL_ENABLE_STEREOEXT
            mix += (short)((accm * channel.RightPan) >> 16);
#else
            mix += (short)(accm & channel.Chb);
#endif
        }
        chip.MixBuff[1] = mix;

#if OPL_QUIRK_CHANNELSAMPLEDELAY
    for (ii = 33; ii < 36; ii++)
    {
        Opl3ProcessSlot(ref chip.Slot[ii]);
    }
#endif

        if ((chip.Timer & 0x3f) == 0x3f) {
            chip.TremoloPos = (byte)((chip.TremoloPos + 1) % 210);
        }
        if (chip.TremoloPos < 105) {
            chip.Tremolo = (byte)(chip.TremoloPos >> chip.TremoloShift);
        } else {
            chip.Tremolo = (byte)((210 - chip.TremoloPos) >> chip.TremoloShift);
        }

        if ((chip.Timer & 0x3ff) == 0x3ff) {
            chip.VibPos = (byte)((chip.VibPos + 1) & 7);
        }

        chip.Timer++;

        chip.EgAdd = 0;
        if (chip.EgTimer > 0) {
            while (shift < 36 && ((chip.EgTimer >> shift) & 1) == 0) {
                shift++;
            }
            if (shift > 12) {
                chip.EgAdd = 0;
            } else {
                chip.EgAdd = (byte)(shift + 1);
            }
        }

        if (chip.EgTimerRem > 0 || chip.EgState > 0) {
            if (chip.EgTimer == 0xfffffffff) {
                chip.EgTimer = 0;
                chip.EgTimerRem = 1;
            } else {
                chip.EgTimer++;
                chip.EgTimerRem = 0;
            }
        }

        chip.EgState ^= 1;
        writebuf = chip.WriteBuf[chip.WriteBufCur];
        while (writebuf.Time <= chip.WritebufSampleCnt) {
            if ((writebuf.Reg & 0x200) <= 0) {
                break;
            }
            writebuf.Reg &= 0x1ff;
            Opl3WriteReg(ref chip, writebuf.Reg, writebuf.Data);
            chip.WriteBufCur = (chip.WriteBufCur + 1) % OplWriteBufSize;
        }
        chip.WritebufSampleCnt++;
    }

    public static void Opl3GenerateResampled(ref Opl3Chip chip, short[] buf, uint bufOffset) {
        while (chip.SampleCnt >= chip.RateRatio) {
            chip.OldSamples[0] = chip.Samples[0];
            chip.OldSamples[1] = chip.Samples[1];
            Opl3Generate(ref chip, chip.Samples);
            chip.SampleCnt -= chip.RateRatio;
        }
        buf[bufOffset + 0] = (short)((chip.OldSamples[0] * (chip.RateRatio - chip.SampleCnt)
                         + chip.Samples[0] * chip.SampleCnt) / chip.RateRatio);
        buf[bufOffset + 1] = (short)((chip.OldSamples[1] * (chip.RateRatio - chip.SampleCnt)
                         + chip.Samples[1] * chip.SampleCnt) / chip.RateRatio);
        chip.SampleCnt += 1 << RsmFrac;
    }

    public static void Opl3Reset(ref Opl3Chip chip, uint samplerate) {
        Opl3Slot slot;
        Opl3Channel channel;
        byte slotNum;
        byte channelNum;
        byte localChannelSlot;

        for (slotNum = 0; slotNum < 36; slotNum++) {
            slot = chip.Slot[slotNum];
            slot.Chip = chip;
            slot.Mod = chip.ZeroMod;
            slot.EgRout = 0x1ff;
            slot.EgOut = 0x1ff;
            slot.EgGen = (byte)EnvelopeGenNum.Release;
            slot.Trem = (byte)chip.ZeroMod;
            slot.SlotNum = slotNum;
        }

        /* (DOSBox Staging addition)
         * The number of channels is not defined as a self-documenting constant
         * variable and instead is represented by hardcoded literals (18) throughout
         * the code. Therefore, we programmatically determine the total number of
         * channels available and double check it against this magic literal.
         */
         if(chip.Channel.Length != 18) {
             throw new UnrecoverableException($"{nameof(chip.Channel)} must equal 18");
         }

        for (channelNum = 0; channelNum < 18; channelNum++) {
            channel = chip.Channel[channelNum];
            localChannelSlot = ChSlot[channelNum];
            channel.Slots[0] = chip.Slot[localChannelSlot];
            channel.Slots[1] = chip.Slot[localChannelSlot + 3];
            chip.Slot[localChannelSlot].Channel = channel;
            chip.Slot[localChannelSlot + 3].Channel = channel;
            if ((channelNum % 9) < 3) {
                /* (DOSBox Staging addition) */
                int index = channelNum + 3;
                // assert(index < channels);
                channel.Pair = chip.Channel[index];
            } else if ((channelNum % 9) < 6) {
                /* (DOSBox Staging addition) */
                int index = channelNum - 3;
                // assert(index >= 0 && index < channels);
                channel.Pair = chip.Channel[index];
            }
            channel.Chip = chip;
            channel.Out[0] = chip.ZeroMod;
            channel.Out[1] = chip.ZeroMod;
            channel.Out[2] = chip.ZeroMod;
            channel.Out[3] = chip.ZeroMod;
            channel.ChType = (byte)ChType.Ch2Op;
            channel.Cha = 0xffff;
            channel.Chb = 0xffff;
#if OPL_ENABLE_STEREOEXT
            channel.leftpan = 0x10000;
            channel.rightpan = 0x10000;
#endif
            channel.ChNum = channelNum;
            Opl3ChannelSetupAlg(channel);
        }
        chip.Noise = 1;
        chip.RateRatio = (int)((samplerate << RsmFrac) / 49716);
        chip.TremoloShift = 4;
        chip.VibShift = 1;

#if OPL_ENABLE_STEREOEXT
        if (!PanpotLutBuild)
        {
            int i;
            for (i = 0; i < 256; i++)
            {
                PanpotLut[i] = OplSin(i);
            }
            PanpotLutBuild = 1;
        }
#endif
    }

    public static void Opl3WriteReg(ref Opl3Chip chip, ushort reg, byte v) {
        byte high = (byte)((reg >> 8) & 0x01);
        byte regm = (byte)(reg & 0xff);
        switch (regm & 0xf0) {
            case 0x00:
                if (high > 0) {
                    switch (regm & 0x0f) {
                        case 0x04:
                            Opl3ChannelSet4Op(ref chip, v);
                            break;
                        case 0x05:
                            chip.NewM = (byte)(v & 0x01);
#if OPL_ENABLE_STEREOEXT
                            chip.StereoExt = (v >> 1) & 0x01;
#endif
                            break;
                    }
                } else {
                    switch (regm & 0x0f) {
                        case 0x08:
                            chip.Nts = (byte)((v >> 6) & 0x01);
                            break;
                    }
                }
                break;
            case 0x20:
            case 0x30:
                if (AdSlot[regm & 0x1f] >= 0) {
                    Opl3SlotWrite20(ref chip.Slot[18 * high + AdSlot[regm & 0x1f]], v);
                }
                break;
            case 0x40:
            case 0x50:
                if (AdSlot[regm & 0x1f] >= 0) {
                    Opl3SlotWrite40(ref chip.Slot[18 * high + AdSlot[regm & 0x1f]], v);
                }
                break;
            case 0x60:
            case 0x70:
                if (AdSlot[regm & 0x1f] >= 0) {
                    Opl3SlotWrite60(ref chip.Slot[18 * high + AdSlot[regm & 0x1f]], v);
                }
                break;
            case 0x80:
            case 0x90:
                if (AdSlot[regm & 0x1f] >= 0) {
                    Opl3SlotWrite80(ref chip.Slot[18 * high + AdSlot[regm & 0x1f]], v);
                }
                break;
            case 0xe0:
            case 0xf0:
                if (AdSlot[regm & 0x1f] >= 0) {
                    Opl3SlotWriteE0(ref chip.Slot[18 * high + AdSlot[regm & 0x1f]], v);
                }
                break;
            case 0xa0:
                if ((regm & 0x0f) < 9) {
                    Opl3ChannelWriteA0(chip.Channel[9 * high + (regm & 0x0f)], v);
                }
                break;
            case 0xb0:
                if (regm == 0xbd && high <= 0) {
                    chip.TremoloShift = (byte)((((v >> 7) ^ 1) << 1) + 2);
                    chip.VibShift = (byte)(((v >> 6) & 0x01) ^ 1);
                    Opl3ChannelUpdateRhytm(ref chip, v);
                } else if ((regm & 0x0f) < 9) {
                    Opl3ChannelWriteB0(chip.Channel[9 * high + (regm & 0x0f)], v);
                    if ((v & 0x20) > 0) {
                        OPL3ChannelKeyOn(chip.Channel[9 * high + (regm & 0x0f)]);
                    } else {
                        Opl3ChannelKeyOff(chip.Channel[9 * high + (regm & 0x0f)]);
                    }
                }
                break;
            case 0xc0:
                if ((regm & 0x0f) < 9) {
                    Opl3ChannelWriteC0(chip.Channel[9 * high + (regm & 0x0f)], v);
                }
                break;
#if OPL_ENABLE_STEREOEXT
            case 0xd0:
                if ((regm & 0x0f) < 9)
                {
                    OPL3ChannelWriteD0(chip.Channel[9 * high + (regm & 0x0f)], v);
                }
            break;
#endif
        }
    }

    public static void Opl3WriteRegBuffered(ref Opl3Chip chip, ushort reg, byte v) {
        ulong time1, time2;
        Opl3WriteBuf writebuf;
        uint writeBufLast = chip.WriteBufLast;
        writebuf = chip.WriteBuf[writeBufLast];

        if ((writebuf.Reg & 0x200) > 0) {
            Opl3WriteReg(ref chip, (ushort)(writebuf.Reg & 0x1ff), writebuf.Data);

            chip.WriteBufCur = (writeBufLast + 1) % OplWriteBufSize;
            chip.WritebufSampleCnt = writebuf.Time;
        }

        writebuf.Reg = (ushort)(reg | 0x200);
        writebuf.Data = v;
        time1 = chip.WriteBufLastTime + OplWriteBufDelay;
        time2 = chip.WritebufSampleCnt;

        if (time1 < time2) {
            time1 = time2;
        }

        writebuf.Time = time1;
        chip.WriteBufLastTime = time1;
        chip.WriteBufLast = (writeBufLast + 1) % OplWriteBufSize;
    }

    public static void Opl3GenerateStream(ref Opl3Chip chip, short[] sndptr, uint numsamples) {
        uint i;
        uint sndOffset = 0;
        for (i = 0; i < numsamples; i++) {
            Opl3GenerateResampled(ref chip, sndptr, sndOffset);
            sndOffset += 2;
        }
    }
}
public struct Opl3Chip {
    public Opl3Chip() {
        for(int i = 0; i < Channel.Length; i++) {
            Channel[i] = new();
        }
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
    public short[] OldSamples { get; set; } = new short[2];

    public short[] Samples { get; set; } = new short[2];

    public ulong WritebufSampleCnt { get; set; }

    public uint WriteBufCur { get; set; }

    public uint WriteBufLast { get; set; }

    public ulong WriteBufLastTime { get; set; }

    public Opl3WriteBuf[] WriteBuf { get; set; } = new Opl3WriteBuf[Opl3Nuked.OplWriteBufSize];
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

    public byte RegRr { get; set; }
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
    public Opl3Chip Chip { get; set; }

    public short[] Out { get; set; } = new short[4];
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
