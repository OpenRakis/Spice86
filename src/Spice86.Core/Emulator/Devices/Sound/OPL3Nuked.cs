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
#undef OPL_ENABLE_STEREOEXT
//#define OPL_ENABLE_STEREOEXT

namespace Spice86.Core.Emulator.Devices.Sound;

public static class OPL3Nuked {
    public const int OPL_WRITEBUF_SIZE = 1024;
    public const int OPL_WRITEBUF_DELAY = 2;
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
