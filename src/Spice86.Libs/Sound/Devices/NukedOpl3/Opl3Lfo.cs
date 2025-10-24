// SPDX-FileCopyrightText: 2013-2025 Nuked-OPL3 by nukeykt
// SPDX-License-Identifier: LGPL-2.1

namespace Spice86.Libs.Sound.Devices.NukedOpl3;

/// <summary>
///     Encapsulates Nuked-OPL3 LFO state transitions (tremolo and vibrato).
/// </summary>
internal static class Opl3Lfo {
    /* Original C: chip->tremolo = 0; chip->tremolopos = 0; chip->tremoloshift = 4;
     *             chip->vibpos = 0; chip->vibshift = 1;
     */
    internal static void Reset(Opl3Chip chip) {
        chip.Tremolo = 0;
        chip.TremoloPosition = 0;
        chip.TremoloShift = 4;
        chip.VibratoPosition = 0;
        chip.VibratoShift = 1;
    }

    /* Original C: tremolo/vibrato update inside OPL3_Generate */
    internal static void Advance(Opl3Chip chip) {
        if ((chip.Timer & 0x3f) == 0x3f) {
            chip.TremoloPosition = (byte)((chip.TremoloPosition + 1) % 210);
        }

        if (chip.TremoloPosition < 105) {
            chip.Tremolo = (byte)(chip.TremoloPosition >> chip.TremoloShift);
        } else {
            chip.Tremolo = (byte)((210 - chip.TremoloPosition) >> chip.TremoloShift);
        }

        if ((chip.Timer & 0x3ff) == 0x3ff) {
            chip.VibratoPosition = (byte)((chip.VibratoPosition + 1) & 0x07);
        }
    }

    /* Original C: chip->tremoloshift = (((v >> 7) ^ 1) << 1) + 2;
     *             chip->vibshift = ((v >> 6) & 1) ^ 1;
     */
    internal static void ConfigureDepth(Opl3Chip chip, byte value) {
        chip.TremoloShift = (byte)((((value >> 7) ^ 1) << 1) + 2);
        chip.VibratoShift = (byte)(((value >> 6) & 0x01) ^ 1);
    }
}